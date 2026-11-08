# 物品公司

基于本机游戏 0.46D 的原版 `GameItem.EnableManufacturer` 和 `manufacturer` Tag。`items.csv` 第十一列 `manufacturer` 填写公司标识，留空不设置公司；公司归属由通用工厂初始化，不在 Helper 内按物品 ID 分派。

| 标识 | 中文名称 | 英文名称 |
| --- | --- | --- |
| `age_of_agi.astar` | A星智能 | Astar Intelligence |
| `age_of_agi.prism` | 棱镜半导体 | Prism Semiconductors |
| `age_of_agi.silicon` | 斯立科半导体 | Silicon Semiconductors |
| `solana` | 索拉纳科技（原版） | 沿用原版本地化 |

当前内容分配：GPU1～3、compiler、invalid_accesscard 属于 A星智能；CPU0、GPU0、MEM0 属于斯立科半导体；交换机、其余 CPU/GPU 属于棱镜半导体；脑机接口、电子眼球、brain_server 使用原版索拉纳科技。未指定的物品留空。

原版公司行位于物品名称下方。原版 DisplayManufacturer 写死了公司名称分支，因此 ManufacturerTooltip 在该方法的 Prefix 中仅处理三家自有公司，使用本地化、原版青绿色和下划线；原版索拉纳及其他公司走原生显示。停用模组时补丁放行。

公司 Tag 沿原生存档保存和恢复，不在 onLoaded 重写实例已有公司，不新增历史存档迁移。独立行为检查使用合成物品和文本，验证空列、字段格式、创建、存档字段保留、双语名称与补丁作用范围；构建引用 0.46D，真实游戏提示框仍需目视核验。
