// 用途：点歌 Lite mod 的配置（MelonPreferences，落 Mods/SongRequestLite.cfg 之类）。
// 状态：现行。
using MelonLoader;

namespace SongRequestMod
{
    internal static class Config
    {
        /// <summary>整个 mod 的开关</summary>
        public static bool Enable = true;

        /// <summary>HTTP 接口端口（只给别的程序调，没有网页）</summary>
        public static int Port = 8790;

        /// <summary>是否同时监听局域网 IP（手机/另一台电脑来调；无鉴权，别在公共网络开）</summary>
        public static bool LanAccess = true;

        /// <summary>详细日志：跳曲每一步都打</summary>
        public static bool VerboseLog = false;

        /// <summary>跳曲后停在难度选择页（而不是直接进谱）</summary>
        public static bool JumpToDifficultyScreen = true;

        /// <summary>结算成绩回传地址；空 = 自动（回到最近一次发 /jump 的那台机器的 CallbackPort）</summary>
        public static string ScoreCallback = "";

        /// <summary>自动回传时对方机器上的端口（测试脚本默认监听 20010）</summary>
        public static int CallbackPort = 20010;

        public static void Load()
        {
            var c = MelonPreferences.CreateCategory("SongRequestLite", "点歌（Lite）");
            Enable = c.CreateEntry("启用", true, "整个 mod 的开关").Value;
            Port = c.CreateEntry("接口端口", 8790, "HTTP 接口端口（POST /jump，GET /status）").Value;
            LanAccess = c.CreateEntry("局域网", true, "同时监听局域网 IP（无鉴权）").Value;
            VerboseLog = c.CreateEntry("详细日志", false, "输出跳曲每一步").Value;
            JumpToDifficultyScreen = c.CreateEntry("跳到难度页", true, "跳曲后停在难度选择页").Value;
            ScoreCallback = (c.CreateEntry("回传地址", "",
                "手动覆盖回传地址；留空 = 自动（回到给你发 /jump 的那台机器）").Value ?? "").Trim();
            CallbackPort = c.CreateEntry("回传端口", 20010,
                "自动回传用的端口（对方机器上监听成绩的端口）").Value;

            MelonPreferences.Save();   // 让 cfg 文件立刻落盘（否则要等游戏正常退出才写）
        }
    }
}
