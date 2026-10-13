using System.Collections.Generic;
using System.Linq;
using Mod.ModMenu;
using Mod.PickMob;
using UnityEngine;

namespace Mod.Auto
{
    internal static class NROPrivateAutoZone
    {
        internal static bool ChangeZoneWhenNoMob;
        internal static bool RandomZone = true;
        internal static bool AvoidPreviousZone = true;

        private const int ID_ICON_ITEM_TDLT = 4387;
        private const long EMPTY_CONFIRM_MS = 1800L;
        private const long CHANGE_COOLDOWN_MS = 7000L;
        private const long VERIFY_TIMEOUT_MS = 5000L;

        private static long emptySince;
        private static long lastRequestAt;
        private static long pendingSince;
        private static int lastObservedZone = -1;
        private static int previousZone = -1;
        private static int pendingZone = -1;

        internal static void SetEnabled(bool value)
        {
            ChangeZoneWhenNoMob = value;
            Rms.saveRMSInt("nro_private_no_mob_zone", value ? 1 : 0);
            if (!value) ResetTransient();
        }

        internal static void SetRandomZone(bool value)
        {
            RandomZone = value;
            Rms.saveRMSInt("nro_private_random_zone", value ? 1 : 0);
        }

        internal static void SetAvoidPreviousZone(bool value)
        {
            AvoidPreviousZone = value;
            Rms.saveRMSInt("nro_private_avoid_previous_zone", value ? 1 : 0);
        }

        internal static void Load()
        {
            ChangeZoneWhenNoMob = Rms.loadRMSInt("nro_private_no_mob_zone") == 1;
            int random = Rms.loadRMSInt("nro_private_random_zone");
            int avoid = Rms.loadRMSInt("nro_private_avoid_previous_zone");
            RandomZone = random == -1 || random == 1;
            AvoidPreviousZone = avoid == -1 || avoid == 1;
        }

        internal static void Update()
        {
            if (!ChangeZoneWhenNoMob || !Pk9rPickMob.IsTanSat)
            {
                ResetTransient();
                return;
            }

            Char me = Char.myCharz();
            if (me == null || me.cHP <= 0 || me.statusMe == 14) return;
            if (GameCanvas.isLoading || Controller.isStopReadMessage) return;

            int currentZone = TileMap.zoneID;
            if (lastObservedZone < 0) lastObservedZone = currentZone;

            if (currentZone != lastObservedZone)
            {
                previousZone = lastObservedZone;
                lastObservedZone = currentZone;
                pendingZone = -1;
                pendingSince = 0L;
                emptySince = 0L;
                return;
            }

            long now = mSystem.currentTimeMillis();
            if (pendingZone >= 0)
            {
                if (now - pendingSince < VERIFY_TIMEOUT_MS) return;
                pendingZone = -1;
                pendingSince = 0L;
            }

            if (HasEligibleMob())
            {
                emptySince = 0L;
                return;
            }

            if (emptySince == 0L)
            {
                emptySince = now;
                return;
            }

            if (now - emptySince < EMPTY_CONFIRM_MS || now - lastRequestAt < CHANGE_COOLDOWN_MS) return;

            int targetZone = PickTargetZone(currentZone);
            if (targetZone < 0 || targetZone == currentZone) return;

            pendingZone = targetZone;
            pendingSince = now;
            lastRequestAt = now;
            Service.gI().requestChangeZone(targetZone, 0);
        }

        internal static bool HasEligibleMob()
        {
            if (GameScr.vMob == null) return true;

            bool avoidSuper = Pk9rPickMob.IsNeSieuQuai && !ItemTime.isExistItem(ID_ICON_ITEM_TDLT);
            bool sawRuntimeMob = false;

            for (int i = 0; i < GameScr.vMob.size(); i++)
            {
                Mob mob = GameScr.vMob.elementAt(i) as Mob;
                if (mob == null) continue;
                sawRuntimeMob = true;

                if (mob.status == 0 || mob.status == 1 || mob.hp <= 0 || mob.isMobMe) continue;
                if (mob.levelBoss != 0 && avoidSuper) continue;
                if (Pk9rPickMob.IdMobsTanSat.Count != 0 && !Pk9rPickMob.IdMobsTanSat.Contains(mob.mobId)) continue;
                if (Pk9rPickMob.TypeMobsTanSat.Count != 0 && !Pk9rPickMob.TypeMobsTanSat.Contains(mob.getTemplate().mobTemplateId)) continue;
                return true;
            }

            if (GameScr.vMob.size() > 0 && !sawRuntimeMob) return true;
            return false;
        }

        private static int PickTargetZone(int currentZone)
        {
            GameScr game = GameScr.gI();
            if (game == null || game.zones == null || game.zones.Length == 0) return -1;

            List<int> candidates = new List<int>();
            for (int i = 0; i < game.zones.Length; i++)
            {
                int zone = game.zones[i];
                if (zone == currentZone) continue;
                if (AvoidPreviousZone && zone == previousZone) continue;
                if (game.numPlayer != null && game.maxPlayer != null && i < game.numPlayer.Length && i < game.maxPlayer.Length && game.maxPlayer[i] > 0 && game.numPlayer[i] >= game.maxPlayer[i]) continue;
                candidates.Add(zone);
            }

            if (candidates.Count == 0) return -1;
            return RandomZone ? candidates[Res.random(0, candidates.Count)] : candidates[0];
        }

        private static void ResetTransient()
        {
            emptySince = 0L;
            pendingZone = -1;
            pendingSince = 0L;
        }
    }

    internal sealed class NROPrivateAutoTrainBootstrap : MonoBehaviour
    {
        private static bool created;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (created) return;
            created = true;
            NROPrivateAutoZone.Load();
            GameObject go = new GameObject("NRO_PRIVATE_AutoTrain");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<NROPrivateAutoTrainBootstrap>();
        }

        private void Update()
        {
            EnsureMenuItems();
            if (Main.started && GameCanvas.currentScreen is GameScr) NROPrivateAutoZone.Update();
        }

        private static void EnsureMenuItems()
        {
            ModMenuItemBoolean[] items = ModMenuMain.modMenuItemBools;
            if (items == null) return;

            const string idEnabled = "NROPrivate_ChangeZoneWhenNoMob_Toggle";
            if (items.Any(x => x != null && x.ID == idEnabled)) return;

            ModMenuItemBoolean[] extra =
            {
                new ModMenuItemBoolean(new ModMenuItemBooleanConfig
                {
                    ID = idEnabled,
                    Title = "Hết quái đổi khu",
                    Description = "Tự đổi sang khu khác khi không còn quái hợp lệ để Tàn Sát.",
                    GetValueFunc = () => NROPrivateAutoZone.ChangeZoneWhenNoMob,
                    SetValueAction = NROPrivateAutoZone.SetEnabled
                }),
                new ModMenuItemBoolean(new ModMenuItemBooleanConfig
                {
                    ID = "NROPrivate_RandomZone_Toggle",
                    Title = "Random khu",
                    Description = "Chọn ngẫu nhiên trong các khu còn chỗ.",
                    GetValueFunc = () => NROPrivateAutoZone.RandomZone,
                    SetValueAction = NROPrivateAutoZone.SetRandomZone,
                    GetIsDisabled = () => !NROPrivateAutoZone.ChangeZoneWhenNoMob,
                    GetDisabledReason = () => "Bật Hết quái đổi khu trước"
                }),
                new ModMenuItemBoolean(new ModMenuItemBooleanConfig
                {
                    ID = "NROPrivate_AvoidPreviousZone_Toggle",
                    Title = "Không quay lại khu vừa đứng",
                    Description = "Loại khu vừa rời khỏi danh sách khu kế tiếp.",
                    GetValueFunc = () => NROPrivateAutoZone.AvoidPreviousZone,
                    SetValueAction = NROPrivateAutoZone.SetAvoidPreviousZone,
                    GetIsDisabled = () => !NROPrivateAutoZone.ChangeZoneWhenNoMob,
                    GetDisabledReason = () => "Bật Hết quái đổi khu trước"
                })
            };

            ModMenuMain.modMenuItemBools = items.Concat(extra).ToArray();
        }
    }
}
