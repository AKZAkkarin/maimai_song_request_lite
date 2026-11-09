# Changelog

## v0.1.9

对应 Maimai Planner 0.2.0。

### 新增

- 增加 `/save` 接口，从游戏当前内存读取 1P 存档，供 Web 拉取用户和成绩。
- 增加 `/status` 中的 `resultSerial`、`loginSerial`、`isEntry`、`userName` 等状态字段。
- 增加 `/score` 单条成绩查询接口。
- 增加 `/wait-select` 长轮询接口，等待回到可点歌的选曲界面。
- 增加结算界面检测和 `/score`、`/save` 查询。
- 增加读取存档时的 `dataid` 上限配置。

### 改进

- 完善跳曲状态机和界面切换处理。
- 改善点歌、成绩查询和助手联调时的状态反馈。

## v0.1.2

- 首个基本功能可用的版本，对应 Maimai Planner 0.1.0。
