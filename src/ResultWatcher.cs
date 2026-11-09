// 用途：监听真正的结算界面，并缓存登录状态；不读成绩、不联网。
// 状态：0.1.9 基线；ResultProcess.OnStart 是唯一可靠的结算信号。
using System;
using HarmonyLib;
using Manager;
using MAI2.Util;

namespace SongRequestMod
{
    [HarmonyPatch(typeof(global::Process.ResultProcess), "OnStart")]
    internal static class Patch_ResultOnStart
    {
        private static void Postfix()
        {
            ResultWatcher.OnResult();
        }
    }

    internal static class ResultWatcher
    {
        /// <summary>进入结算界面的次数。</summary>
        internal static int ResultSerial;

        /// <summary>登录/退出/换人的次数。</summary>
        internal static int LoginSerial;

        internal static string UserName = "";
        internal static bool IsEntry;

        private static int _lastSampleAt;

        internal static void OnResult()
        {
            ResultSerial++;
            ModLog.Info("进入结算界面，resultSerial=" + ResultSerial);
        }

        /// <summary>每 10 秒最多采样一次登录状态；登录/换人/退出都能覆盖。</summary>
        internal static void Tick()
        {
            int now = Environment.TickCount;
            if (_lastSampleAt != 0 && unchecked(now - _lastSampleAt) < 10000)
            {
                return;
            }
            _lastSampleAt = now;

            bool entry = false;
            string name = "";
            try
            {
                var mgr = Singleton<UserDataManager>.Instance;
                var ud = mgr == null ? null : mgr.GetUserData(0);
                if (ud != null)
                {
                    entry = ud.IsEntry;
                    if (entry && ud.Detail != null)
                    {
                        name = ud.Detail.UserName ?? "";
                    }
                }
            }
            catch
            {
            }

            if (entry != IsEntry || name != UserName)
            {
                IsEntry = entry;
                UserName = name;
                LoginSerial++;
                ModLog.Info("登录状态变化，loginSerial=" + LoginSerial
                    + " entry=" + (entry ? "true" : "false")
                    + " name=" + name);
            }
        }
    }
}
