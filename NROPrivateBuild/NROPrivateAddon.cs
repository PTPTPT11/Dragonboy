using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class NROPrivateAddon
{
    private const long EmptyConfirmMs = 1800;
    private const long ChangeCooldownMs = 7000;
    private const long VerifyTimeoutMs = 5000;
    private static bool initialized;
    private static bool menuInjected;
    private static bool enabled;
    private static bool randomZone = true;
    private static bool avoidPrevious = true;
    private static long emptySince;
    private static long lastRequest;
    private static long pendingSince;
    private static int lastZone = -1;
    private static int previousZone = -1;
    private static int pendingZone = -1;
    private static readonly Random rng = new Random();
    private static Assembly gameAsm;
    private static Type pickMobType;
    private static Type itemTimeType;

    public static void Tick()
    {
        try
        {
            if (!initialized) Initialize();
            if (!menuInjected) TryInjectMenu();
            UpdateAutoZone();
        }
        catch
        {
            // Fail closed: never break the game render/update loop.
        }
    }

    private static void Initialize()
    {
        initialized = true;
        gameAsm = typeof(GameScr).Assembly;
        pickMobType = gameAsm.GetType("Mod.PickMob.Pk9rPickMob", false);
        itemTimeType = gameAsm.GetType("ItemTime", false);
        enabled = LoadFlag("nro_private_no_mob_zone", false);
        randomZone = LoadFlag("nro_private_random_zone", true);
        avoidPrevious = LoadFlag("nro_private_avoid_previous_zone", true);
    }

    private static bool LoadFlag(string key, bool fallback)
    {
        try
        {
            int v = Rms.loadRMSInt(key);
            if (v == -1) return fallback;
            return v == 1;
        }
        catch { return fallback; }
    }

    private static void SaveFlag(string key, bool value)
    {
        try { Rms.saveRMSInt(key, value ? 1 : 0); } catch { }
    }

    private static bool IsTanSatEnabled()
    {
        if (pickMobType == null) return false;
        return ReadStaticBool(pickMobType, "IsTanSat", false);
    }

    private static bool AvoidSuperMob()
    {
        if (pickMobType == null) return false;
        bool avoid = ReadStaticBool(pickMobType, "IsNeSieuQuai", false);
        if (!avoid) return false;
        try
        {
            if (itemTimeType != null)
            {
                MethodInfo m = itemTimeType.GetMethod("isExistItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null && (bool)m.Invoke(null, new object[] { 4387 })) return false;
            }
        }
        catch { }
        return true;
    }

    private static bool ReadStaticBool(Type t, string name, bool fallback)
    {
        try
        {
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (p != null) return Convert.ToBoolean(p.GetValue(null, null));
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return Convert.ToBoolean(f.GetValue(null));
        }
        catch { }
        return fallback;
    }

    private static object ReadStaticObject(Type t, string name)
    {
        try
        {
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (p != null) return p.GetValue(null, null);
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return f.GetValue(null);
        }
        catch { }
        return null;
    }

    private static void UpdateAutoZone()
    {
        if (!enabled || !IsTanSatEnabled())
        {
            ResetTransient();
            return;
        }

        Char me = Char.myCharz();
        if (me == null || me.cHP <= 0 || me.statusMe == 14) return;
        if (GameCanvas.isLoading || Controller.isStopReadMessage) return;

        int currentZone = TileMap.zoneID;
        if (lastZone < 0) lastZone = currentZone;
        if (currentZone != lastZone)
        {
            previousZone = lastZone;
            lastZone = currentZone;
            pendingZone = -1;
            pendingSince = 0;
            emptySince = 0;
            return;
        }

        long now = mSystem.currentTimeMillis();
        if (pendingZone >= 0)
        {
            if (now - pendingSince < VerifyTimeoutMs) return;
            pendingZone = -1;
            pendingSince = 0;
        }

        if (HasEligibleMob())
        {
            emptySince = 0;
            return;
        }

        if (emptySince == 0)
        {
            emptySince = now;
            return;
        }

        if (now - emptySince < EmptyConfirmMs || now - lastRequest < ChangeCooldownMs) return;
        int target = PickTargetZone(currentZone);
        if (target < 0 || target == currentZone) return;

        pendingZone = target;
        pendingSince = now;
        lastRequest = now;
        Service.gI().requestChangeZone(target, 0);
    }

    private static bool HasEligibleMob()
    {
        if (GameScr.vMob == null) return true;
        bool avoidSuper = AvoidSuperMob();
        HashSet<int> ids = ReadIntSet("IdMobsTanSat");
        HashSet<int> types = ReadIntSet("TypeMobsTanSat");

        for (int i = 0; i < GameScr.vMob.size(); i++)
        {
            Mob mob = GameScr.vMob.elementAt(i) as Mob;
            if (mob == null) continue;
            if (mob.status == 0 || mob.status == 1 || mob.hp <= 0 || mob.isMobMe) continue;
            if (avoidSuper && mob.levelBoss != 0) continue;
            if (ids != null && ids.Count > 0 && !ids.Contains(mob.mobId)) continue;
            if (types != null && types.Count > 0)
            {
                MobTemplate template = mob.getTemplate();
                if (template == null || !types.Contains(template.mobTemplateId)) continue;
            }
            return true;
        }
        return false;
    }

    private static HashSet<int> ReadIntSet(string name)
    {
        object value = pickMobType == null ? null : ReadStaticObject(pickMobType, name);
        if (value == null) return null;
        HashSet<int> result = new HashSet<int>();
        IEnumerable enumerable = value as IEnumerable;
        if (enumerable == null) return result;
        foreach (object o in enumerable)
        {
            try { result.Add(Convert.ToInt32(o)); } catch { }
        }
        return result;
    }

    private static int PickTargetZone(int current)
    {
        GameScr gs = GameScr.gI();
        if (gs == null || gs.zones == null || gs.zones.Length == 0) return -1;
        List<int> candidates = new List<int>();
        for (int i = 0; i < gs.zones.Length; i++)
        {
            int z = gs.zones[i];
            if (z == current) continue;
            if (avoidPrevious && z == previousZone) continue;
            if (gs.numPlayer != null && gs.maxPlayer != null && i < gs.numPlayer.Length && i < gs.maxPlayer.Length && gs.maxPlayer[i] > 0 && gs.numPlayer[i] >= gs.maxPlayer[i]) continue;
            candidates.Add(z);
        }
        if (candidates.Count == 0) return -1;
        return randomZone ? candidates[rng.Next(candidates.Count)] : candidates[0];
    }

    private static void ResetTransient()
    {
        emptySince = 0;
        pendingZone = -1;
        pendingSince = 0;
    }

    private static void TryInjectMenu()
    {
        if (gameAsm == null) return;
        Type menuType = gameAsm.GetType("Mod.ModMenu.ModMenuMain", false);
        Type itemType = gameAsm.GetType("Mod.ModMenu.ModMenuItemBoolean", false);
        Type cfgType = gameAsm.GetType("Mod.ModMenu.ModMenuItemBooleanConfig", false);
        if (menuType == null || itemType == null || cfgType == null) return;
        FieldInfo itemsField = menuType.GetField("modMenuItemBools", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (itemsField == null) return;
        Array oldItems = itemsField.GetValue(null) as Array;
        if (oldItems == null) return;

        for (int i = 0; i < oldItems.Length; i++)
        {
            object obj = oldItems.GetValue(i);
            if (obj == null) continue;
            PropertyInfo idp = obj.GetType().BaseType.GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (idp != null && Convert.ToString(idp.GetValue(obj, null)) == "NROPrivate_ChangeZoneWhenNoMob_Toggle")
            {
                menuInjected = true;
                return;
            }
        }

        object a = CreateBoolMenuItem(itemType, cfgType, "NROPrivate_ChangeZoneWhenNoMob_Toggle", "Hết quái đổi khu", "Đổi khu khi Tàn Sát không còn quái hợp lệ.", () => enabled, v => { enabled = v; SaveFlag("nro_private_no_mob_zone", v); if (!v) ResetTransient(); }, null, null);
        object b = CreateBoolMenuItem(itemType, cfgType, "NROPrivate_RandomZone_Toggle", "Random khu", "Chọn ngẫu nhiên khu còn chỗ.", () => randomZone, v => { randomZone = v; SaveFlag("nro_private_random_zone", v); }, () => !enabled, () => "Bật Hết quái đổi khu trước");
        object c = CreateBoolMenuItem(itemType, cfgType, "NROPrivate_AvoidPreviousZone_Toggle", "Không quay lại khu vừa đứng", "Không chọn lại khu vừa rời.", () => avoidPrevious, v => { avoidPrevious = v; SaveFlag("nro_private_avoid_previous_zone", v); }, () => !enabled, () => "Bật Hết quái đổi khu trước");
        if (a == null || b == null || c == null) return;

        Array next = Array.CreateInstance(itemType, oldItems.Length + 3);
        Array.Copy(oldItems, next, oldItems.Length);
        next.SetValue(a, oldItems.Length);
        next.SetValue(b, oldItems.Length + 1);
        next.SetValue(c, oldItems.Length + 2);
        itemsField.SetValue(null, next);
        menuInjected = true;
    }

    private static object CreateBoolMenuItem(Type itemType, Type cfgType, string id, string title, string description, Func<bool> getter, Action<bool> setter, Func<bool> disabled, Func<string> reason)
    {
        try
        {
            object cfg = Activator.CreateInstance(cfgType, true);
            SetProperty(cfgType, cfg, "ID", id);
            SetProperty(cfgType, cfg, "Title", title);
            SetProperty(cfgType, cfg, "Description", description);
            SetProperty(cfgType, cfg, "GetValueFunc", getter);
            SetProperty(cfgType, cfg, "SetValueAction", setter);
            if (disabled != null) SetProperty(cfgType, cfg, "GetIsDisabled", disabled);
            if (reason != null) SetProperty(cfgType, cfg, "GetDisabledReason", reason);
            ConstructorInfo ctor = itemType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { cfgType }, null);
            return ctor == null ? null : ctor.Invoke(new object[] { cfg });
        }
        catch { return null; }
    }

    private static void SetProperty(Type t, object obj, string name, object value)
    {
        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p == null && t.BaseType != null) p = t.BaseType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) p.SetValue(obj, value, null);
    }
}
