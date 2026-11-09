# SongRequestLite

当前版本：`0.1.2`

模组功能：

- 接收 HTTP 命令，跳到指定曲目/难度；
- 一局结束后，把成绩回传给点歌方；

## 编译

这是源码包，不包含编译好的 DLL。

需要：

- .NET SDK；
- .NET Framework 4.8 目标包；
- 对应游戏版本目录里的 `MelonLoader` 和 `Sinmai_Data/Managed` 程序集。

编译命令：

```powershell
dotnet build SongRequestLite.csproj -c Release -p:GameDir="<游戏目录>\Package"
```

产物：

```text
bin/Release/SongRequestLite.dll
```

把该 DLL 放进游戏的 `Mods/`，启动游戏即可。首次启动会在 `Package/UserData/MelonPreferences.cfg` 内生成配置文件。

启动后日志里会出现：

```text
[点歌] 接口: http://127.0.0.1:8790/
```

## 接口

默认地址：

```text
http://127.0.0.1:8790/
```

接口没有鉴权。开启配置里的「局域网」后会同时监听局域网 IP，不建议在公共网络开启。

### POST /jump — 跳歌

请求体：`application/x-www-form-urlencoded`

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | 是 | 游戏曲目 id。DX 行通常 ≥ 10000，例如 15001；STD 例如 5001 |
| `diff` | 否 | `0`=BASIC，`1`=ADVANCED，`2`=EXPERT，`3`=MASTER，`4`=Re:MASTER；省略则跳到最高可用难度 |
| `type` | 否 | `dx` 或 `std`；省略则按 id 自动判断 |

示例：

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:8790/jump `
  -Body @{ id=15001; diff=3; type="dx" }
```

成功响应：

```json
{
  "ok": true,
  "superseded": false,
  "id": 15001,
  "diff": 3,
  "msg": "已跳转: 示例歌曲名 [DX MASTER]",
  "callbackTo": "http://127.0.0.1:20010/api/score"
}
```

说明：

- `superseded: true` 表示这次请求被后续点歌取代，`ok` 会是 `false`；
- 连续发多次 `/jump` 时，只执行最新一次；
- 曲目表还没建立时返回 HTTP 409；
- 游戏数据还在加载时返回 HTTP 503。

### GET /status — 运行状态

示例：

```powershell
Invoke-RestMethod http://127.0.0.1:8790/status
```

响应：

```json
{
  "ok": true,
  "ready": true,
  "version": "0.1.2.0",
  "songs": 1934,
  "inSelect": true,
  "busy": false,
  "callbackTo": "http://127.0.0.1:20010/api/score",
  "lastCaller": "127.0.0.1",
  "lastJump": "已加入跳曲队列",
  "lastJumpAt": "20:00:00"
}
```

| 字段 | 说明 |
|---|---|
| `ready` | 游戏数据是否已经加载完成 |
| `songs` | 当前曲名表里的曲目数量 |
| `inSelect` | 是否处于选曲界面 |
| `busy` | 是否正在执行跳曲 |
| `callbackTo` | 当前成绩回传地址 |
| `lastCaller` | 最近一次调用 `/jump` 的机器 IP |
| `lastJump` / `lastJumpAt` | 最近一次跳曲结果和时间（时间为 `HH:mm:ss`） |

### GET /probe — 当前选曲状态

只读诊断接口，不修改游戏状态。

```powershell
Invoke-RestMethod http://127.0.0.1:8790/probe
```

示例：

```json
{
  "ready": true,
  "scoreType": 0,
  "realId": 10146,
  "id0": 146,
  "id1": 146,
  "existStd": true,
  "existDx": false,
  "canChange": false,
  "category": 32,
  "index": 32,
  "difficulty": 4,
  "cursorId": 146
}
```

| 字段 | 说明 |
|---|---|
| `ready` | 是否处于选曲界面 |
| `scoreType` | 当前选中的 STD/DX 类型 |
| `realId` | 当前卡片的曲目 id |
| `id0` / `id1` | 卡片两个槽位里的真实曲目 id |
| `existStd` / `existDx` | 是否存在 STD / DX 谱面 |
| `canChange` | 当前卡片能否切换 STD/DX |
| `category` / `index` | 当前分类和光标位置 |
| `difficulty` | 当前难度 |
| `cursorId` | 光标所在曲目 id |

`/probe` 不在选曲界面时返回：

```json
{"ready": false}
```

### POST /callback — 临时修改成绩回传地址

请求体：

```text
url=http://127.0.0.1:20010/api/score
```

示例：

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:8790/callback `
  -Body @{ url="http://127.0.0.1:20010/api/score" }
```

响应：

```json
{"ok": true}
```

说明：

- `url` 必须是完整 URL；
- 这个修改只在当前游戏进程内有效，不写配置文件；
- 重启游戏后会恢复配置里的「回传地址」。

## 成绩回传

一局结束后，模组会向回传地址 `POST` 一次 JSON。

回传地址优先级：

```text
/callback 临时设置
→ 配置里的「回传地址」
→ 最近一次调用 /jump 的机器 IP + 「回传端口」
```

默认路径：

```text
http://<最近调用 /jump 的 IP>:20010/api/score
```

请求示例：

```json
{
  "musicId": 15001,
  "name": "示例歌曲名",
  "type": "dx",
  "difficulty": 3,
  "difficultyName": "MASTER",
  "achive": 100.0000,
  "combo": 897,
  "maxCombo": 897,
  "notes": 897,
  "trackNo": 1,
  "judge": {
    "criticalPerfect": 900,
    "trueCritical": 0,
    "perfect": 30,
    "great": 5,
    "good": 1,
    "miss": 2
  }
}
```

| 字段 | 说明 |
|---|---|
| `musicId` | 游戏曲目 id |
| `name` | 曲名 |
| `type` | `dx` 或 `std` |
| `difficulty` | 难度编号 0–4 |
| `difficultyName` | 难度名称 |
| `achive` | 达成率，4 位小数；字段名就是 `achive` |
| `combo` / `maxCombo` | 当前连击 / 最大连击 |
| `notes` | 总物量 |
| `trackNo` | 曲目顺序 |
| `judge` | 判定统计 |

注意：

- 故意不包含玩家名等身份字段；
- 收到方建议按 `musicId + difficulty + trackNo` 去重；
- 回传超时时间为 3 秒。

## 配置

游戏启动后生成， `Package/UserData/MelonPreferences.cfg` 内生成的配置项如下：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| 启用 | true | 整个模组开关 |
| 接口端口 | 8790 | HTTP 接口端口 |
| 局域网 | true | 同时监听局域网 IP；无鉴权 |
| 详细日志 | false | 输出跳曲每一步 |
| 跳到难度页 | true | 跳曲后停在难度选择页 |
| 回传地址 | 空 | 留空则自动回传给最近一次 `/jump` 的机器 |
| 回传端口 | 20010 | 自动回传时对方监听的端口 |

## 说明

- 本包只包含源码、工程文件、README 和许可证，不包含 DLL、构建脚本或打包脚本。

## 来源与许可

本项目核心功能扩展自 [ZCM-X/maimai-song-request-](https://github.com/ZCM-X/maimai-song-request-)。

在保留上游跳曲状态机和主线程调度的基础上，本 Lite 版本主要做了这些修改：

- 精简为只负责“跳曲 + 结算成绩回传”，去掉网页、别名库和游玩面板等非核心功能；
- 增加本机 HTTP 接口 `/jump`、`/status`、`/probe`、`/callback`，供打歌助手调用；
- 增加结算成绩回传，回传内容不包含玩家名等身份字段；
- 针对汉化整合包的 STD/DX 双卡结构和曲目表来源做了适配；
- 配置精简为 MelonPreferences 中的几个开关和端口。

其中 `src/MainThread.cs` 与上游保持一致，`src/SelectDriver.cs` 基于上游核心跳曲状态机改写。

上游项目使用 MIT License，Copyright (c) 2026 ZCM-X。本项目保留上游许可，完整文本见 [LICENSE](LICENSE)。
