// 用途：自用版点歌 mod —— 只保留「收到命令跳歌」+「结算回传成绩」，没有网页/别名/游玩面板。
// 状态：现行（0.1.9）。跳曲状态机 / 主线程队列源自上游 ZCM-X/maimai-song-request（MIT），
//       已针对汉化整合包的 STD/DX 双卡结构和曲目表来源做过修改。
using System;
using System.Reflection;
using MelonLoader;
using HarmonyLib;

[assembly: MelonInfo(typeof(SongRequestMod.Mod), "SongRequestLite", "0.1.9", "")]
[assembly: MelonGame("sega-interactive", "Sinmai")]
[assembly: AssemblyVersion("0.1.9.0")]
[assembly: AssemblyFileVersion("0.1.9.0")]

namespace SongRequestMod
{
    /// <summary>
    /// 只做两件事：
    ///   1) HTTP POST /jump → 跳到指定曲目 + 难度（内部走 SelectDriver 状态机）
    ///   2) 一局打完（离开游玩状态）→ 把这一局的成绩 POST 到配置的回调地址
    /// 全部内存内操作，不改任何游戏文件。
    /// </summary>
    public class Mod : MelonMod
    {
        internal static Mod Instance;
        /// <summary>最近一次收到 /jump 的对方 IP（局域网）；成绩自动回传到这个 IP。</summary>
        internal static string LastCallerIp = "";
        /// <summary>/callback 临时指定的回传 URL（仅接收成绩模式用；不写配置文件）</summary>
        internal static string CallbackOverride = "";

        private static volatile bool _ready;
        private static bool _updateErrorLogged;

        public override void OnInitializeMelon()
        {
            Instance = this;
            MainThread.Init();
            Config.Load();
            try
            {
                HarmonyInstance.PatchAll(typeof(Patch_SelectOnStart));
                HarmonyInstance.PatchAll(typeof(Patch_SelectOnRelease));
                HarmonyInstance.PatchAll(typeof(Patch_ResultOnStart));
                ModLog.Info("Harmony 已挂: MusicSelectProcess / ResultProcess");
            }
            catch (Exception e)
            {
                MelonLogger.Error("挂 Harmony 失败: " + e.Message);
            }
        }

        /// <summary>网页服务放到"晚初始化": 别的 mod 都初始化完了再起，免得抢资源</summary>
        public override void OnLateInitializeMelon()
        {
            if (Config.Enable)
            {
                Web.Start(Config.Port);
            }
        }

        public override void OnUpdate()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                if (!_updateErrorLogged)
                {
                    _updateErrorLogged = true;
                    MelonLogger.Error("OnUpdate 异常(后续不再重复报): " + e.Message);
                }
            }
        }

        /// <summary>游戏数据(DataManager)加载完之前什么都不做，否则会在初始化途中撞上登录流程。</summary>
        internal static bool Ready
        {
            get { return _ready; }
        }

        private static bool GameReady()
        {
            if (_ready)
            {
                return true;
            }
            try
            {
                var dm = MAI2.Util.Singleton<Manager.DataManager>.Instance;
                if (dm == null || !dm.IsLoaded())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            _ready = true;
            return true;
        }

        private void Tick()
        {
            if (!GameReady())
            {
                return;
            }

            // 1) 主线程队列：HTTP 线程交过来的活在这里执行
            MainThread.Pump();

            // 2) 缓存"在不在选曲界面"，点歌请求要靠它
            SelectDriver.UpdateCache();

            // 3) 点歌请求：统一在主线程执行（HTTP 线程直接动游戏对象会崩）
            SelectDriver.RunPending();

            // 4) 每秒采样登录状态；结算由 ResultProcess.OnStart 直接通知
            ResultWatcher.Tick();
        }
    }
}
