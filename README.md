# SongRequestLite 0.1.9

本模组是 [maimai planner](https://github.com/AKZAkkarin/maimai_planner) 项目的配套组件，用于在舞萌 DX 内接收点歌指令并读取 1P 存档数据。

本项目以 MIT License 发布，可自由使用、修改和分发。

模组功能：

- 接收 HTTP 命令，跳到指定曲目/难度；
- 从游戏当前内存读取 1P 存档，供 WebUI 拉取；
- 其他接口详见下文

## 编译

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
  "msg": "已跳转: 示例歌曲名 [DX MASTER]"
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
  "version": "0.1.9.0",
  "songs": 1934,
  "inSelect": true,
  "resultSerial": 3,
  "loginSerial": 1,
  "isEntry": true,
  "userName": "玩家名",
  "busy": false,
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
| `resultSerial` | 进入结算界面的次数（ResultProcess.OnStart） |
| `loginSerial` / `isEntry` / `userName` | 登录状态采样；换人时 userName 变化 |
| `busy` | 是否正在执行跳曲 |
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

### GET /score — 查询单条成绩

只读查询指定曲目和难度的单条成绩，不扫描整份存档。

maimai planner 当前通过 `/status` + `/save` 同步成绩，不依赖此接口；保留供外部工具查询或调试使用。

```powershell
Invoke-RestMethod "http://127.0.0.1:8790/score?internalId=11491&chartId=3"
```

```json
{
  "ok": true,
  "found": true,
  "internalId": 11491,
  "chartId": 3,
  "achievement": 100.1234,
  "playCount": 2,
  "rank": 5,
  "combo": 3,
  "sync": 1,
  "deluxScore": 1234
}
```

### GET /wait-select — 等待可点歌

长轮询：请求会一直挂着，直到玩家回到可点歌的选曲界面、并且跳曲队列空闲，才返回。

```powershell
Invoke-RestMethod "http://127.0.0.1:8790/wait-select?timeout=30000"
```

可点歌：

```json
{"ok":true,"ready":true,"inSelect":true,"busy":false,"waitedMs":1200}
```

超时：

```json
{"ok":true,"ready":false,"inSelect":false,"busy":false,"timeout":true,"waitedMs":30000}
```

| 参数 | 说明 |
|---|---|
| `timeout` | 最长等待毫秒数，默认 30000，范围 100–120000 |

打歌助手会在每次 `/jump` 前先调用这个接口；只有 `ready=true` 才推歌，避免在结算界面或切场景时盲推。

### GET /save — 当前 1P 存档

从游戏当前已加载的 `UserData` 读取 1P 成绩，只读，不会修改游戏存档。

```powershell
Invoke-RestMethod http://127.0.0.1:8790/save
```

返回：

```json
{
  "ok": true,
  "source": "game-save",
  "filter": {"dataIdMax": 5000},
  "user": {"index": 0, "name": "玩家名", "rating": 15000},
  "count": 1234,
  "scores": [
    {
      "internalId": 10001,
      "dataId": 1,
      "chartId": 3,
      "achievement": 100.1234,
      "rank": 5,
      "combo": 3,
      "sync": 0,
      "playCount": 42,
      "deluxScore": 1234
    },
    {
      "internalId": 111355,
      "dataId": 1355,
      "chartId": 0,
      "achievement": 171.9538,
      "rank": 5,
      "combo": 0,
      "sync": 5,
      "playCount": 1,
      "deluxScore": 2742
    }
  ]
}
```

规则：

- 只读取 1P。（2P 的支持方法有待考虑，大概率不会搞）
- `dataId` 等于 `internalId % 10000`；
- `dataId > 5000` 的成绩视为自制谱，一律不输出
- 字段 `achivement` 拼写是游戏原字段，输出已改为 `achievement`；
- 未记录的谱面不会出现在 `scores` 里；
- 仅读游戏内存，不会修改存档文件。

## 历史接口：成绩回传

旧版本中，模组会在对局结束后向 `POST /callback` 设置的地址发送成绩 JSON。

maimai planner 现在已改为通过 `/status` + `/save` 同步成绩，不再需要该回传接口。`/callback`、相关回传配置和 `callbackTo` 字段在模组 0.1.9 中仍保留，将在 **模组 0.2.0 彻底删除**。

## 配置

配置文件：

```text
<游戏目录>/Package/UserData/MelonPreferences.cfg
```

游戏启动后生成，`[SongRequestLite]` 分类下的配置项如下：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| 启用 | true | 整个模组开关 |
| 接口端口 | 8790 | HTTP 接口端口 |
| 局域网 | true | 同时监听局域网 IP；无鉴权 |
| 详细日志 | false | 输出跳曲每一步 |
| 跳到难度页 | true | 跳曲后停在难度选择页 |
| 回传地址 | 空 | 历史配置，模组 0.2.0 删除 |
| 回传端口 | 20010 | 历史配置，模组 0.2.0 删除 |
| 存档dataid上限 | 5000 | 读取当前存档时，dataid > 此值的谱面不输出 |

### 局域网

`局域网 = false` 时只监听：

```text
http://127.0.0.1:8790/
```

只有本机可以调用接口。

`局域网 = true` 时还会监听当前机器所有物理网卡的 IPv4 地址，例如：

```text
http://127.0.0.1:8790/
http://<局域网IP>:8790/
```

虚拟网卡以及 `127.x`、`169.254.x` 地址会被过滤。

> ⚠️ 接口没有鉴权。开启局域网后，同一网络内的其他设备可以调用跳曲、读取存档等接口。建议只在可信网络开启，不要在公共 Wi-Fi 或直接暴露到公网的环境使用。

修改配置后需要重启游戏才会生效。

## 来源与许可

本项目核心功能扩展自 [ZCM-X/maimai-song-request-](https://github.com/ZCM-X/maimai-song-request-)。

在保留上游跳曲状态机和主线程调度的基础上，本 Lite 版本主要做了这些修改：

- 精简为只负责“跳曲 + 状态查询 + 存档读取”，去掉网页、别名库和游玩面板等非核心功能；
- 增加本机 HTTP 接口 `/jump`、`/status`、`/probe`、`/score`、`/wait-select`、`/save`；
- 增加结算界面检测和 `/score`、`/save` 查询；历史成绩回传接口已废弃；
- 针对汉化整合包的 STD/DX 双卡结构和曲目表来源做了适配；
- 配置精简为 MelonPreferences 中的几个开关和端口。

其中 `src/MainThread.cs` 与上游保持一致，`src/SelectDriver.cs` 基于上游核心跳曲状态机改写。

上游项目使用 MIT License，Copyright (c) 2026 ZCM-X。本项目保留上游许可，完整文本见 [LICENSE](LICENSE)。

## 使用许可

本项目以 MIT License 发布，可自由使用、修改、分发，也可直接调用接口或将源码用于其他项目。再分发时请保留 [LICENSE](LICENSE) 中的版权和许可声明。
