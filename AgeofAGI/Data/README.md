# 配置表

`Data` 下的配置统一为 UTF-8 CSV。`items.csv` 保存通用物品映射和本体价格；`Definitions/` 保存分类参数、配方和数据内容；`night_shift.csv` 和 `electronics_entry.csv` 保存两级电子产业的礼包和供货。存档内的训练、生产与夜班状态仍使用原有 JSON 格式。

## 格式约定

- 首个非空、非注释行是表头，字段名大小写敏感。分类表按列名读取，允许重排列；每行必须保留全部列。`items.csv` 继续使用固定列序。
- 使用逗号分列、不带引号的字段，小数点固定为 `.`。允许 UTF-8 BOM、空行和以 `#` 开头的整行注释；字段两侧空白会去除。
- 必须填写数字列；整数不得使用小数或指数格式，浮点数必须有限。保留原字段名；`taskType`、`sprite_prefix` 等按其定义允许为空。
- 定义 ID 忽略大小写检查重复；引用必须与 `items.csv` 匹配。业务范围与原配置相同。错误会指出文件、行号和相关字段。
- 行顺序决定磁盘写入器选项顺序和夜班确定性抽取顺序，调整夜班行序或随机池顺序会改变后续抽取结果。
- 所有配置按注册批次读取；构建部署后需重启游戏。修改初始参数不会覆盖旧存档物品已保存的属性或价格。

## 表职责

| 文件 | 内容 |
| --- | --- |
| `items.csv` | ID、kind、图片、Int64 本体价格、本地化键、显示类别、audio_preset音效预设、contraband_level违禁等级与manufacturer公司 |
| `Definitions/ai_parts.csv` | 组件角色、基础算力、内存、独立的训练量与训练数据需求、任务类型 |
| `Definitions/base_model.csv` | 基模网格宽高与模型内存乘数 |
| `Definitions/compiler.csv` | 单一整网格配方及耐久、连接、反馈、正则化参数 |
| `Definitions/cpu.csv` | CPU 百分比加成及耗电 |
| `Definitions/gpu.csv` | GPU 算力及耗电 |
| `Definitions/memory.csv` | 内存容量 |
| `Definitions/machines.csv` | 机器初始单次耗电 |
| `Definitions/disk_writer.csv` | 五种配方 ID 对应的编解码器产物 |
| `Definitions/server.csv` | 训练、硬件扩建、情报／生物加工及数据抽取参数 |
| `Definitions/encrypted_items.csv` | 待加工物品ID、工作量、任务头条件及加工后的物品ID（目前为普通／稀有BTC） |
| `Definitions/data_carriers.csv` | 载体/容器容量、初始含量、数据单价和状态图前缀 |
| `night_shift.csv` | 二级夜班制造礼包与每周供货 |
| `electronics_entry.csv` | 一级电子商人礼包、硬件／模型／BTC周货 |
| `electronics_event_stock.csv` | 五组市场事件的静态出售货单与容器数据比例 |

## 物品音效列

`items.csv` 固定十一列，第九列为必填 `audio_preset`，第十列为必填 `contraband_level`，第十一列 `manufacturer` 可留空。每件物品可独立选择原版音效预设；名称区分大小写、统一使用小写，允许字段两侧空白。音效填写 `none` 保持原生默认值，空值或未知名称会报告文件和行号。

例如身份卡可选择 `plastic_card`，文件夹可选择 `paper_sheet`，金属小件可选择 `tiny_metal`。修改这列不会改变物品类型、数据容量或功能。完整预设列表与映射位于 `Items/ItemAudioPresets.cs`；注册时解析为委托，由所属物品Helper调用一次。`small_machine`／`metal_machine` 也设置开关声音，`scanner` 也设置激活声音；`dossier` 仅设置开关声音。

## 物品公司列

公司列 `manufacturer` 的空值表示不设置公司。`age_of_agi.astar`、`age_of_agi.prism`、`age_of_agi.silicon` 为新增公司，`solana` 使用原版索拉纳科技。详见[公司字段](../Docs/Items/Manufacturers.md)。

## 物品违禁等级列

`contraband_level` 统一使用小写，允许两侧空白。只能填写以下五种值，空值或其他名称会报告文件、行号和字段。无需在 `types` 中另加违禁类别，工厂会调用原版 `ContrabandHelper` 同时初始化类别、标记及等级。

| 配置值 | 含义 | 原版等级 |
| --- | --- | --- |
| `none` | 非违禁 | 无违禁状态 |
| `low` | 低级 | `CONTRABAND_LEVEL_LOW`，1 |
| `mid` | 中级 | `CONTRABAND_LEVEL_MID`，2 |
| `high` | 高级 | `CONTRABAND_LEVEL_HIGH`，3 |
| `critical` | 高危级 | `CONTRABAND_LEVEL_CRITICAL`，4 |

2026-11-05，按本机游戏0.46D原版接口接入。现有物品初始配置均为 `none`，可逐行调整。设置用于新建实例；读取存档保留实例已保存的违禁状态。

## 夜班列

```csv
section,itemIds,count,chance,dataAmount,minimumProductionSpeed,memoryGb,computeBonus,durability,trainingWork,trainingData
gifts,age_of_agi.disk_empty,4,100,-1,0,0,0,0,0,0
stock,age_of_agi.ai_untrained,1,100,-1,0,6,40,7,120,30
```

`section` 只能是 `gifts` 或 `stock`，两组都必须存在。`itemIds` 用分号连接等概率互斥的物品 ID；每行独立判定 `chance`（0～100 整数百分比），命中后给出 `count` 件（1～100）。`dataAmount=-1` 使用物品初始数据，非负值可用于数据容器，或礼包服务器（装入节点8的普通容器）；`minimumProductionSpeed=0` 表示无额外速度门槛。礼包每行必须为单一物品、100% 概率、速度门槛 0。

模型行必须填写memoryGb、computeBonus、durability，分别为正Int32内存需求、正的性能加成和正Int32耐久，不设置8GB内存或100耐久的平衡上限，也不要求赠品适配入门硬件。未训练模型还须正trainingWork和正Int32 trainingData；已训练模型两项为0。非模型行五项均填0，模型只生成计算型。服务器按实际安装内存判断能否使用模型。属性随待来访货单和礼包冻结并存档，实例创建不重读配置。价格只在items.csv设置，不加进供货或模型参数列。完整货单和兼容规则见[电子商人](../Npcs/Electronics/README.md)与[夜班](../Npcs/Nightshift/NightShift.md)。

构建会移除输出与部署目录中本次迁移的旧 JSON 配置，以及旧位置的 `Data/data_content.csv` 和更名前的 `Data/Definitions/data_content.csv`；其他文件沿用现有部署规则。

## 加密物品加工

```csv
item_id,work,task_type,output_item_id
age_of_agi.btc_normal_unfinish,40,computing,age_of_agi.btc_normal
age_of_agi.btc_rare_unfinish,2400,computing,age_of_agi.btc_rare
```

`item_id`为原物品ID，`work`为有限正工作量，`task_type`为已训练AI必须具备的任务类型，`output_item_id`为加工后的物品ID。任务类型使用`computing`、`intelligence_intrusion`、`biological_analysis`或`sensory_analysis`等模型内部键，不填写任务头物品ID；默认计算型模型无需额外任务头组件。每个原物品只能定义一条配方，原物品和产物必须是items.csv中已注册的data_item且ID不同。默认两条BTC配方均需computing；不新增供货来源或其他延期玩法。

每批注册加载一次，服务器实例不重读表。新加工任务保存版本2的encrypted_item状态，冻结输入、任务类型、工作量及OutputItemId，跨夜只在完成交付时消耗一个原物品和一次AI耐久，不收训练数据费。修改表后，已开始的任务继续原工作量和原产物；新任务使用新表。版本1的btc_normal／btc_rare任务保留原进度和原产物，不重置或转换。

编解码器的 `trainingWork` 与 `trainingData` 分别配置工作量和数据需求，功能组件均填0。反馈只减少前者；训练使用模型独立数据预算，不提供服务器统一费率或旧档迁移。公式及当前格式规则见 [Training.md](../Docs/Items/Training.md)。

## 随机电子供货（2026-11-06）

`electronics_visit_rules.csv` 保存seller、min_count、max_count，控制尝试件数。随机货池统一保存在 `electronics_suppliers.csv`（电子设备供货商表），列为seller、slot、probability、min_reputation、item_ids。每个卖家配置low／medium／high，小偷另配reputation槽；每次先按概率选槽，再在item_ids内均匀选一件。概率合计100，声望槽未解锁时对其他槽归一。只有声望槽允许非零min_reputation，默认黑市声望60。零概率槽可以为空，正概率槽必须有商品，同一卖家不能重复配置同一物品。每个注册批次只读取和解析一次供货商表，六类卖家共享该实例。显卡供货商使用gpu_supplier槽位并固定每访4件。完整清单见[槽位货单](../Npcs/Electronics/StockProbabilities.md)。

初始分档根据本体价加原始载体75%容量数据的参考价值；实际分档由CSV决定，调价不自动移动槽位。整批／单件价格上限已移除。交换机、空文件夹、复杂数据容器已从全部当前供货清单移除；夜班周货删除复杂数据容器，固定周商其余货物继续用原机制。不诚实普通商仍固定首位GPU1；企业计算服务商不再出售空容器。默认测试使用独立槽位样例，不依赖本目录可调数值。

## 电子市场事件

`electronics_event_stock.csv`保存`seller,item_id,count,data_fraction`。`count`展开为独立交付项；`data_fraction=-1`保留工厂初始状态，0～1仅用于原始载体／数据容器并按容量冻结数据量。五组事件各持续2天，每天等概率生成1名专属客人，全部卖家启用原生整包批发。事件价格、收购范围、完整货单和条件食物交易见[电子市场事件](../Npcs/Electronics/Events.md)。该表每个注册批次读取一次，默认测试使用独立样例。

## 客人对话映射

`client_dialogues.csv`保存`client_id,variant,greeting_key,deal_complete_key`，覆盖收购商、供货商、周商和剧情客人。同一客人可有多个正整数variant，创建时等概率选择；每个客人的variant不得重复。对话键只允许字母、数字和下划线，开场键必填，成交键留空则保留原生成交对白和回调。文本在`Localization/Npcs/en.csv`与`zh.csv`中配置，每个名字后紧跟该客人的可用对白；构建时合并进原生语言表。收购商由头按分号逐句显示，末段实时报告预算，供货商和剧情客人不追加预算。成交链保留首节点，在末段调用原生`OnDealAccepted`；开场末段绑定供货或离场动作。每个注册批次加载一次，禁用时释放映射。详见[本地化源表](../Localization/README.md)。
