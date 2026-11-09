// 用途：从游戏当前已加载的 UserData 读取 1P 玩家成绩，供 WebUI 拉取。
//       dataId = internalId 的最后 4 位；dataId > 5000 一律截断，不区分谱面类型。
// 状态：新项目基线；只读内存，不修改游戏存档。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Manager;

namespace SongRequestMod
{
    internal static class SaveReader
    {
        private const int PlayerIndex = 0;

        /// <summary>
        /// 当前 1P 的存档快照。成功和失败都返回 JSON，网页端用 ok 判断。
        /// dataId = internalId % 10000；dataId > Config.DataIdMax 的谱面不输出。
        /// </summary>
        internal static string CaptureJson()
        {
            try
            {
                var mgr = MAI2.Util.Singleton<UserDataManager>.Instance;
                if (mgr == null)
                {
                    return Error("游戏数据还没加载");
                }

                var ud = mgr.GetUserData((long)PlayerIndex);
                if (ud == null)
                {
                    return Error("1P 没有加载玩家存档");
                }
                if (!ud.IsEntry)
                {
                    return Error("1P 当前没有登录玩家");
                }

                var detail = ud.Detail;
                string name = detail == null ? "" : (detail.UserName ?? "");
                int rating = detail == null ? 0 : (int)detail.Rating;

                var scores = new List<string>();
                var dic = ud.ScoreDic;
                int dataIdMax = Config.DataIdMax;
                if (dic != null)
                {
                    for (int diff = 0; diff < dic.Length; diff++)
                    {
                        var byMusic = dic[diff];
                        if (byMusic == null)
                        {
                            continue;
                        }
                        foreach (var kv in byMusic)
                        {
                            var us = kv.Value;
                            if (us == null)
                            {
                                continue;
                            }

                            int internalId = kv.Key;
                            int dataId = internalId % 10000;
                            if (dataId > dataIdMax)
                            {
                                continue;
                            }

                            var sb = new StringBuilder(256);
                            sb.Append('{');
                            sb.Append("\"internalId\":").Append(internalId);
                            sb.Append(",\"dataId\":").Append(dataId);
                            sb.Append(",\"chartId\":").Append(diff);
                            sb.Append(",\"achievement\":")
                              .Append(((double)us.achivement / 10000.0).ToString("0.0000", CultureInfo.InvariantCulture));
                            sb.Append(",\"rank\":").Append((int)us.scoreRank);
                            sb.Append(",\"combo\":").Append((int)us.combo);
                            sb.Append(",\"sync\":").Append((int)us.sync);
                            sb.Append(",\"playCount\":").Append(us.playcount);
                            sb.Append(",\"deluxScore\":").Append(us.deluxscore);
                            sb.Append('}');
                            scores.Add(sb.ToString());
                        }
                    }
                }

                var outSb = new StringBuilder(64 * 1024);
                outSb.Append('{');
                outSb.Append("\"ok\":true");
                outSb.Append(",\"source\":\"game-save\"");
                outSb.Append(",\"generatedAt\":\"")
                     .Append(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))
                     .Append('"');
                outSb.Append(",\"filter\":{\"dataIdMax\":").Append(dataIdMax).Append('}');
                outSb.Append(",\"user\":{");
                outSb.Append("\"index\":").Append(PlayerIndex);
                outSb.Append(",\"name\":\"").Append(SongTable.Escape(name)).Append('"');
                outSb.Append(",\"rating\":").Append(rating);
                outSb.Append('}');
                outSb.Append(",\"count\":").Append(scores.Count);
                outSb.Append(",\"scores\":[").Append(string.Join(",", scores)).Append(']');
                outSb.Append('}');
                return outSb.ToString();
            }
            catch (Exception e)
            {
                return Error("读取存档失败: " + e.Message);
            }
        }

        /// <summary>只读一条成绩，给结算后轮询 playCount 用；不扫描整份存档。</summary>
        internal static string ScoreJson(int internalId, int chartId)
        {
            try
            {
                var mgr = MAI2.Util.Singleton<UserDataManager>.Instance;
                if (mgr == null)
                {
                    return "{\"ok\":false,\"error\":\"游戏数据还没加载\"}";
                }
                var ud = mgr.GetUserData((long)PlayerIndex);
                if (ud == null || !ud.IsEntry)
                {
                    return "{\"ok\":true,\"found\":false,\"error\":\"当前没有登录玩家\"}";
                }
                var dic = ud.ScoreDic;
                Manager.UserDatas.UserScore us = null;
                if (dic != null && chartId >= 0 && chartId < dic.Length && dic[chartId] != null)
                {
                    dic[chartId].TryGetValue(internalId, out us);
                }
                if (us == null)
                {
                    return "{\"ok\":true,\"found\":false,\"internalId\":" + internalId
                        + ",\"chartId\":" + chartId + "}";
                }
                var sb = new StringBuilder(256);
                sb.Append('{');
                sb.Append("\"ok\":true,\"found\":true");
                sb.Append(",\"internalId\":").Append(internalId);
                sb.Append(",\"chartId\":").Append(chartId);
                sb.Append(",\"achievement\":")
                  .Append(((double)us.achivement / 10000.0).ToString("0.0000", CultureInfo.InvariantCulture));
                sb.Append(",\"playCount\":").Append(us.playcount);
                sb.Append(",\"rank\":").Append((int)us.scoreRank);
                sb.Append(",\"combo\":").Append((int)us.combo);
                sb.Append(",\"sync\":").Append((int)us.sync);
                sb.Append(",\"deluxScore\":").Append(us.deluxscore);
                sb.Append('}');
                return sb.ToString();
            }
            catch (Exception e)
            {
                return "{\"ok\":false,\"error\":\"" + SongTable.Escape("读取单曲成绩失败: " + e.Message) + "\"}";
            }
        }

        private static string Error(string message)
        {
            return "{\"ok\":false,\"error\":\"" + SongTable.Escape(message) + "\"}";
        }
    }
}
