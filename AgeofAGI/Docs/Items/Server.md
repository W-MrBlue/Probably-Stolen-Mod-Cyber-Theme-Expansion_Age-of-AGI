# Serverbox 开发与实现说明

2026-11-07 CPU扩建拆为两步：第一步3个普通电子元件将1×1扩至2×1，第二步再5个扩至2×2，总计8个。成本分别配置于cpuUpgradeUnitsStep1、cpuUpgradeUnitsStep2。CPU和GPU扩建提示仅显示当前阶段的已投入/阶段成本；切换阶段进度从0重新显示，全部完成后隐藏该行。内存扩建完成也隐藏进度，不增加阶段说明或完成文案。当前格式保存累计材料数，加载按两阶段门槛恢复形状及进度。

## 当前代码结构（2026-11-07）

本次调整保持游戏 0.46D 的物品行为、存档字段和库存索引；功能实现统一进入 Items/Logic，Helper 保留初始化、回调绑定与生命周期入口。架构规则见 [目录说明](README.md)。下文历史实现记录中的 Helper 职责以此表为准。

| 文件 | 当前职责 |
| --- | --- |
| [ServerHelper.cs](../../Items/Helpers/ServerHelper.cs) | 服务器初始化与读档绑定 |
| [MachineRuntime.cs](../../Items/Logic/Machines/MachineRuntime.cs) | 与终端共用的训练、夜间工作、窗口和锁定编排 |
| [ServerState.cs](../../Items/Logic/Server/ServerState.cs) | 标记、槽位索引和安装项查询 |
| [ServerProduction.cs](../../Items/Logic/Server/ServerProduction.cs) | 成品 AI 生产及抽取事务 |
| [ServerTooltip.cs](../../Items/Logic/Server/ServerTooltip.cs) | 进度和硬件属性显示 |
| [ServerUpgrades.cs](../../Items/Logic/Server/ServerUpgrades.cs) | 材料升级与原生材料补丁 |


2026-11-07 分布式网络：具有高速连接升级的交换机可对等配对，服务器可向分布式处理终端提供基础硬件算力与内存，本地可执行任务优先。服务器自己的 AI 不参与集群倍率，也不因贡献硬件消耗耐久；终端统一使用自身模型。连接的数据存储器提供共享数据及容量。规则、槽位和结算说明见 [DistributedTerminal.md](DistributedTerminal.md)。下文高速连接效果延期的旧说明由此更新。

2026-11-03 训练数据更新：新模型分别保存训练量W与数据需求D，S01从节点8按 `Ceil(D × 当前进度 / W)` 累计扣费。缺本夜所需数据时暂停，不扣电，允许换容器续训；空壳保留。旧模型与旧任务继续沿用每单位数据工作量的原公式，不追补旧进度费用。详细公式见 [Training.md](Training.md)，货源见 [NightShift.md](../../Npcs/Nightshift/NightShift.md)。

2026-11-01 数据抽取补充：在原有八个库存节点之后追加节点8「数据容器」（1×3），原索引不变。计算／情报入侵 AI 可分批抽取数据载体，容器可随时更换；批内锁定、批间解锁，直接增加容器内容与价值，不经过普通输出。下文旧八槽描述均指保留的原始节点；当前结构与数值以 [DataExtraction.md](DataExtraction.md) 为准。原文件夹情报配方的交换机升级要求不适用于数据抽取。

整理日期：2026-11-01。项目：Probably Stolen 的 Age of AGI 模组；原版 API 核对基线为游戏 0.46D。本文按当前源码记录已实现行为，作为服务器功能的维护入口。生产任务详情及开发期参数见 [Production.md](Production.md)。

## 1. 当前实现范围

服务器的物品 ID 为 `age_of_agi.serverbox`，CSV 中 `kind=machine`、`types=machine`。支持 S01 模型训练，以及成品AI执行普通／稀有BTC加工、机密情报写入、基因数据提取。安装硬件和对应AI，向输入区放入耗材，过夜推进；生物分析另需选择基因条目。

| 功能 | 当前状态 |
| --- | --- |
| 电池、CPU、GPU、内存、交换机、模型、输入和输出槽 | 已实现 |
| 原版材料交互扩建 CPU/GPU 槽 | 已实现 |
| 按硬件算力跨夜训练、暂停、存档恢复 | 已实现 |
| 成品继承任务、内存、性能和耐久 | 已实现 |
| 机器属性页、任务进度条 | 已实现 |
| 训练曲线模拟显示 | 已永久移除，仅保留实际进度 |
| 交换机模块升级与配方查询接口 | 已实现；网络访问＋包体伪装为情报生产条件 |
| 成品 AI 的部署与生产配方 | 已实现BTC、情报、基因；感知分析用途延期 |
| 数据容器与输入耗材 | 数据容器为训练供给数据，普通输入耗材不参与训练 |
| 机箱阶段图0～2 | 已实现 |
| 散热器、取消任务按钮 | 未实现；散热器槽已移除 |

机箱按显卡槽内容切换：没有物品时使用 `serverbox_stage0`；有物品且实际占格总数不超过12格时使用 `serverbox_stage1`；超过12格时使用 `serverbox_stage2`。按每件物品的实际形状统计非空格，不按包围矩形面积、堆叠数量或槽位容量计算。放入、取出、库存状态更新及读档后刷新，保留机箱位置、旋转、翻转和占格。三张图均为64×80；阶段3～4素材已移除，各阶段都没有独立物品 ID。

## 2. 代码分层与注册

| 文件 | 职责 |
| --- | --- |
| [ItemFactoryDirectory.cs](../../Items/ItemFactoryDirectory.cs) | `RegisterServer` 在注册期加载配置并绑定初始化步骤 |
| [CatalogItemFactory.cs](../../Items/CatalogItemFactory.cs) | 创建物品、应用共用名称、价格、素材并调用唯一所属Helper |
| [ServerCatalog.cs](../../Items/Catalogs/ServerCatalog.cs) | 读取服务器 JSON，严格检查字段及 CSV 映射 |
| [ServerDefinition.cs](../../Items/Definitions/ServerDefinition.cs) | 保存不可变配置，检查数值范围 |
| [MachineParameters.cs](../../Items/MachineParameters.cs) | 通用机器Tag和参数读写；由所属物品Helper调用 |
| [ServerHelper.cs](../../Items/Helpers/ServerHelper.cs) | 唯一服务器物品初始化、音效、读档及提示框入口；实现分布在 MachineRuntime、ServerState、ServerUpgrades、ServerTooltip |
| [ServerHardware.cs](../../Items/Logic/Server/ServerHardware.cs) | 汇总 CPU/GPU/内存和耗电，复核硬件实例与属性 |
| [NetworkSwitchHelper.cs](../../Items/Helpers/NetworkSwitchHelper.cs) / [NetworkSwitchUpgrades.cs](../../Items/Logic/NetworkSwitch/NetworkSwitchUpgrades.cs) | 交换机初始化与回调绑定 / 模块升级、查询和提示框 |
| [ServerTrainingState.cs](../../Items/Logic/Server/ServerTrainingState.cs) | 任务快照、实际进度、有效夜数、数据费用及序列化校验 |
| [AiModelState.cs](../../Items/Logic/AiModel/AiModelState.cs) | 模型校验、属性读写、成品转换和本地化名称 |
| [ModDebugActions.cs](../../Debugging/ModDebugActions.cs) | F9 生成服务器和测试模型 |
| [ServerChecks.cs](../../../Tests/ItemCatalogChecks/ServerChecks.cs) | 服务器行为、库存顺序、实际进度和旧存档兼容性检查 |

初始化顺序是共用物品字段 → `ServerHelper.Init`。ServerHelper 先应用通用机器参数，再创建共享 MachineRuntime，调用 Bind 并绑定 OnLoaded；具体交互、UI 与夜间执行位于运行类。配置在注册批次中读取并捕获于工厂闭包，创建每个实例时不重新读取 JSON。

服务器恢复原版 `MACHINE` 类别和容器、电池槽标记，移动音效使用 `AudioHelper.InitMetalMachine`。它不注册 `STANDARD_MACHINE_TAG`，也不通过原版 `machinery` 机制自动每日扣电：服务器有独立硬件槽，不能套用原版固定模块槽的索引假设。

## 3. 窗口布局与存储索引

窗口使用原版 Machinery UI，横向单排显示：

```text
电池 → CPU → 显卡 → 内存 → 交换机 → 模型 → 输入 → 数据容器 → 输出 → 基因选择
```

“基因选择”只在放入生物分析成品AI时显示，无库存，不改变存储索引。

直接将物品拖到服务器本体时，电池、CPU、显卡、内存、交换机、模型和数据容器优先进入对应专用槽。实现使用原版库存的 `slotPriorityFunc`：专用槽优先级2，普通输入优先级1。专用槽已满、尺寸不符或被锁定时，再尝试其他允许接收的槽；优先级不绕过白名单、容量和任务锁。直接拖入窗口内的输入区仍可正常放置物品。

### 3.1 存档中的固定槽位顺序

| 存储索引 | 槽位 | 初始尺寸 | 接受规则与用途 |
| --- | --- | --- | --- |
| 0 | 电池 | 1×2 单槽 | 原版 `power_source_item` 电源，必须属于玩家 |
| 1 | CPU | 1×1 网格 | 玩家拥有的 CPU，每件数量为1；可安装多颗，受空间限制 |
| 2 | GPU | 2×6 网格 | 玩家拥有的 GPU，每件数量为1；必要时旋转后放入 |
| 3 | 交换机 | 2×2 单槽 | 玩家拥有的网络交换机，每件数量为1；可留空 |
| 4 | 内存 | 4×2 网格 | 玩家拥有的内存条，每件数量为1；累计8个金手指扩为4×4 |
| 5 | 模型 | 2×2 单槽 | 玩家拥有的未训练或成品 AI，每件数量为1 |
| 6 | 输入 | 4×4 网格 | 接受玩家物品，S01 不读取、不消耗此槽物品，数据费从节点8支付 |
| 7 | 输出 | 4×4 网格 | 只允许服务器内部写入成品，玩家可取出 |
| 8 | 数据容器 | 1×3 单槽 | 玩家拥有的普通／高容量数据容器，抽取期间仍可更换 |

显示顺序与存储顺序不同。代码通过 `CreateMachineryWindow` 的显示排序数组 `0,1,2,4,3,5,6,9,7,8` 将内存放到显卡旁边、交换机放到内存后面，并将末尾新增的数据容器面板显示在输入旁。基因面板不含存储。

**这个数组重排的是面板，不是存储节点。** 原版 `CollectStorage` 保留原0～7顺序，末尾追加节点8。后续修改 UI 时须保留该顺序，否则原存档中的硬件可能恢复到错误的槽位。散热器槽已彻底移除且不做迁移：包含退休散热器的旧九槽结构仍不兼容，不能与当前新增数据容器的九槽结构混淆。

### 3.2 机器属性页

统计数据放在机器自身提示信息中，槽位窗口不再重复显示统计文字或独立进度条。

| 内容 | 显示规则 |
| --- | --- |
| 有效算力 | 当前硬件每个有效工作夜可推进的真实工作量 |
| 内存 | 已安装内存总容量，单位 GB |
| 所需能量 | `所需能量：{0} /天`；CPU/GPU 采用相同文案 |
| CPU/显卡扩建投入 | 当前累计材料数及总门槛 |
| 就绪 | 运行条件满足，尚未完成首个有效工作夜 |
| 运行中 | 已完成有效工作夜，当前可继续训练，浅绿色 |
| 电量不足 | 缺少可用电池或电量不足，黄色 |
| 内存不足 | CPU、GPU和内存齐备，但模型要求超过已装容量时显示，淡红色；成品模型尚无输入任务时也检查 |
| 其他暂停原因 | 硬件、内存、输出或模型属性问题对应的本地化文字 |
| 任务进度 | 首个有效工作夜后显示真实进度、百分比及原版样式进度条 |

空模型槽不显示“请放入未训练的AI模型”。CPU 汇总倍率只参与内部计算，不单独展示。进度条使用原版水培器同类的 `RichTextBuilder.AddProgressBar`，长度10格，自定义 float Tag 保留小数工作量。

## 4. 硬件汇总与能量结算

```text
GPU基础算力 = 所有已安装GPU的 computePower 之和
CPU倍率 = 1 + 所有已安装CPU的 powerBoostRate 之和 / 100
有效算力 = GPU基础算力 × CPU倍率 × coolingMultiplier
内存容量 = 所有内存条的GB之和
本夜所需能量 = CPU + GPU + 交换机槽已安装物品的 powerPerCycle 之和
本夜真实进度 = Min(总训练量, 原真实进度 + 有效算力)
```

CPU 的120表示 **+120%**，单颗对应×2.2；120与150两颗合计×3.7。GPU算力先求和，再乘一次CPU倍率和散热倍率。未训练模型的预测性能不参与训练速度。

内存只决定容量，不计能量。交换机物品没有耗电标签，能量贡献为0；散热器槽已移除，`coolingMultiplier` 保留为配置项，默认1.0。机器当前能量成本随硬件刷新，不使用 `machines.csv` 中服务器的初始0作为实际训练费用。

### 4.1 当前硬件配置

| 物品后缀 | 基础算力 / CPU加成 / 内存容量 | 每有效工作夜能量 |
| --- | --- | --- |
| `cpu0` | +120% | 2 |
| `cpu1` | +150% | 3 |
| `cpu2` | +300% | 5 |
| `gpu0` | 算力10 | 2 |
| `gpu1` | 算力30 | 3 |
| `gpu2` | 算力50 | 5 |
| `gpu3` | 算力100 | 10 |
| `mem0` | 8 GB | 不计耗电 |
| `mem1` | 16 GB | 不计耗电 |
| `mem2` | 32 GB | 不计耗电 |

对应配置位于 [cpu.csv](../../Data/Definitions/cpu.csv)、[gpu.csv](../../Data/Definitions/gpu.csv)、[memory.csv](../../Data/Definitions/memory.csv)。价格统一来自 [items.csv](../../Data/items.csv)，不放入硬件 JSON 或 Helper。

示例：CPU0＋GPU0＋足量内存，每有效夜推进22，消耗4能量。训练量80需要4个有效工作夜，共消耗16；最后一晚仍支付完整4，溢出的8点工作量不结转。CPU0＋两张GPU0时，每有效夜推进44，消耗6能量，两晚完成80工作量的任务。

## 5. S01训练任务

### 5.1 模型输入与快照

训练输入为 `age_of_agi.ai_untrained`，成品为 `age_of_agi.ai_trained`。`AiModelState.CanTrain` 要求单件未训练AI具有模型标记、版本标记、合法任务类型，以及完整的正数训练量、内存、预测性能和预测初始耐久；浮点数必须有限，内存必须为正整数。

任务类型支持 `computing`、`sensory_analysis`、`intelligence_intrusion`、`biological_analysis`。

模型放入后创建任务快照，记录 `GetUniqueID()` 分配的身份及五项属性。结算时持续核对身份和属性，防止另一个模型继承已有进度。任务开始前可调整硬件；模型的已有属性不随配置修改重新计算。

### 5.2 每轮结算过程

1. 同步模型；检查模型、机器和硬件的玩家所有权、单件数量、CPU/GPU/内存有效性、内存容量及本夜电量。交换机槽可留空或安装合法交换机。
2. 创建临时成品检查输出空间。即使本夜还不能完成训练，也要求输出区能容纳完整产物；探测物品随后销毁。
3. 计算本夜真实进度。进入执行阶段时将任务设为活动状态，锁定CPU、GPU、交换机、内存和模型槽，并禁止扩建。普通输入区仅锁定生产任务正在加工的物品，其余排队物品可取放；训练不锁输入区。电池可替换，输出可取出。
4. 非完成夜通过原版 `CraftingRecipe` 将电池作为带电工具扣费，模型作为保留工具，不消耗模型。
5. 完成夜创建并初始化成品，先预留输出位置，再复核原模型、电池、硬件属性和实例。通过后由原版配方消耗原模型并扣除本夜完整能量。
6. 提交本夜真实进度、数据费用和有效夜数，更新机器显示及电池外观；完成后清理任务并解锁。每轮最多完成一批，不自动接续新模型。

输出转移失败、执行前复核失败或回调重入不会推进进度、扣除本夜费用；未提交的临时成品会清理。由于活动标记在最终提交前设置，已经进入执行阶段后失败的任务可能保持锁定，等待下一轮重试。

### 5.3 暂停与实际进度

不预收整个任务所需能量，只检查本夜费用。硬件、内存、电量或输出空间不足时暂停，空闲与暂停不扣电；已启动任务保留进度和锁定。

首次有效工作前的空转和开始训练后的暂停都不增加实际进度、有效夜数或资源费用。

### 5.4 原版夜间回调与调试结算

`onCycleEndEarlySlotItemFunc` 为新一轮主结算设置运行时标记；`onCycleEndSlotItemFunc` 消费该标记并执行训练。机器另外保存最后结算天数，阻止没有新一轮开始标记的同日重复主回调。

原版正常过夜和 F9 的 `Skip Night (Machines Only)` 都运行早期、主、晚期回调，但后者**不增加游戏日期**。因此每次完整调试结算均可继续推进；不能只按日期拦截，否则会出现“第一轮到44/80，此后一直不动”。有效工作夜数按实际付费执行轮次增长，不修改游戏日历。

## 6. 成品属性继承

| 未训练AI字段 | 成品AI字段 |
| --- | --- |
| 任务类型 | 原值继承，并用于本地化名称 |
| 内存要求GB | 原值继承 |
| 预测计算性能百分比 | 实际计算性能百分比 |
| 预测初始耐久 | 初始耐久和当前耐久，二者初始相等 |

性能仍按 `1 + 百分比 / 100` 换算；+20%对应×1.2。训练不扣预测耐久。成品提示显示任务、内存、实际性能和当前/初始耐久。

## 7. 真实训练进度

机器tooltip显示实际工作量与原生十格进度条，所需能量、内存和内存扩建投入行下各加一个普通空行，使用 `AddLine("", ignoreEmpty: false)`，不添加带高度的占位空格，以免下一行缩进或间距过大。首次有效工作前不显示进度，开始后在暂停时保留，完成后隐藏；该显示不创建库存节点。正在加工的物品同步显示相同进度：服务器及训练模型为「任务进度」，encrypted_item为「解析进度」，data_carrier为「抽取进度」。排队输入不显示进度，完成后清除。

训练曲线模拟显示已永久移除：不保留绘图面板、损失映射、震荡、采样历史、纹理生命周期或预览入口。存档仅保存真实任务状态；旧显示字段被忽略并在重新保存时清除，不影响训练进度与已支付的数据费用。

## 8. 材料扩建

| 原版材料ID | 累计投入 | 结果 |
| --- | --- | --- |
| `common_electronic` | 第一阶段3个 | CPU槽1×1 → 2×1 |
| `common_electronic` | 第二阶段再5个，累计8个 | CPU槽2×1 → 2×2 |
| `metal_ingot` | 第5个 | GPU槽2×6 → 4×6 |
| `metal_ingot` | 第10个 | GPU槽4×6 → 6×6 |
| `age_of_agi.gold_finger` | 8个 | 内存槽4列×2行 → 4列×4行 |

沿用原版材料使用/拖放到目标的交互，每次扣一个单位，可分批投入；计数随机器保存，未达到门槛时不提前扩容。材料和机器均须属于玩家，活动任务禁止扩建，达到上限拒绝继续投入。扩容后额外重验证窗口（`contentWindow.Validate()`，沿用 `MachineRAS.ApplyTankSize` 的原版做法），打开中的 UI 同步新槽宽，不会错位。

`ServerElectronicUpgradePatch` 和 `ServerIngotUpgradePatch` 分别Postfix原版 `MaterialDirectory.Electronic`、`MetalIngot` 工厂。包装只对当前启用模组的服务器目标分支生效；其他原版升级目标保留原回调。服务器分支由目标回调扣料，源回调不重复扣料，并播放原版棘轮音效。加载器自动安装Harmony补丁。

扩建只改变槽位大小，机箱状态图由实际装入显卡的占格决定，当前只使用阶段0～2。

### 8.1 交换机与升级模块

交换机 `age_of_agi.net_switch`（kind `net_switch`，素材 `network_swtich.png`，2×2）是无窗口物品，可安装到服务器的交换机槽，也可留空；安装与否不影响 S01 训练条件、算力或耗电。

升级模块 `age_of_agi.ns_upgrade_module0`～`ns_upgrade_module3`（kind `ns_upgrade_module`）依次为防火墙、高速连接、网络访问、包体伪装，均有独立素材。沿用原版"对目标物品使用"的交互：模块侧声明 `may/canThisTargetItemFunc` 并由源回调执行，交换机侧声明 `may/canItemTargetThisFunc`；每次消耗一个单位并播放原版棘轮音效。双方必须属于玩家，每个模块索引只能安装一次，重复或对其他目标使用会被拒绝。

升级记录为交换机上的 `AGE_OF_AGI_NS_UPGRADES` 位掩码标签（位 i 对应模块 i），随物品存档。交换机 tooltip 只对已安装的模块追加其本地化名称和已安装状态；未安装任何模块时不标注。

配方查询接口：

```text
NetworkSwitchUpgrades.HasUpgrade(交换机物品, 模块索引)   // 空物品/非交换机/越界索引均返回 false
ServerState.GetInstalledSwitch(服务器)                // 交换机槽当前安装的物品
ServerState.HasSwitchUpgrade(服务器, 模块索引)        // 服务器配方的直接查询入口
```

情报生产检查索引2和3，安装后作为长期条件，不按批消耗；网络访问为游戏设定，不进行现实联网。索引0和1的实际效果延期。新增或调整模块时同步 `ModuleCount`、CSV 行和本地化。

## 9. 配置和持久化

### 9.1 服务器配置

[server.csv](../../Data/Definitions/server.csv) 的当前内容：

```csv
id,trainedOutputId,coolingMultiplier,cpuUpgradeUnitsStep1,cpuUpgradeUnitsStep2,gpuUpgradeUnitsPerStep,intelligenceWork,bioBaseWork,bioComplexityWork,dataUnitsPer8Gb,dataWorkPerUnit,memoryUpgradeUnits
age_of_agi.serverbox,age_of_agi.ai_trained,1,3,5,5,480,40,20,8,1,8
```

| 字段 | 当前值 | 含义 |
| --- | --- | --- |
| `id` | `age_of_agi.serverbox` | 普通服务器物品ID |
| `trainedOutputId` | `age_of_agi.ai_trained` | 未训练AI完成训练后的产物ID |
| `coolingMultiplier` | 1 | 硬件算力系数；GPU算力总和×CPU倍率×该系数，用于训练及生产基础算力 |
| `cpuUpgradeUnitsStep1` | 3 | CPU第一阶段的电子元件数量，1×1→2×1 |
| `cpuUpgradeUnitsStep2` | 5 | CPU第二阶段额外电子元件数量，2×1→2×2；不是累计门槛 |
| `gpuUpgradeUnitsPerStep` | 5 | GPU每阶段金属锭数量，两阶段依次扩为4×6、6×6 |
| `intelligenceWork` | 480 | 恶意AI将空文件夹加工为机密情报所需工作量 |
| `bioBaseWork` | 40 | 生化基因提取的基础工作量 |
| `bioComplexityWork` | 20 | 每点种子有效复杂度增加的工作量；总量=基础量+该值×Max(0,有效复杂度) |
| `dataUnitsPer8Gb` | 8 | 每8GB内存对应的标准抽取数据量；批量=Floor(内存GB×该值/8)，再受源载体剩余量限制 |
| `dataWorkPerUnit` | 1 | 抽取每点数据需要的工作量；批次工作量=实际抽取量×该值 |
| `memoryUpgradeUnits` | 8 | 内存区扩建所需金手指数，4×2→4×4 |

当前Catalog只接受一个服务器定义及上述成品ID；服务器与未训练/成品AI均须有匹配的CSV行。散热倍率为有限正数且不大于1；扩建数量为正整数，两级CPU成本之和及两级GPU总数须在Int32范围内。解析器拒绝缺字段、未知字段、重复ID、错误类型等输入。

BTC配方已独立到[encrypted_items.csv](../../Data/Definitions/encrypted_items.csv)，以item_id、work、task_type、output_item_id指定输入、工作量、模型任务头条件和产物，各配方数值以该表为准。载体与容器配置为data_carriers.csv。encrypted_item任务独立冻结OutputItemId、任务类型及工作量。

[machines.csv](../../Data/Definitions/machines.csv) 必须包含服务器条目，初始 `powerPerCycle=0`；实例能量需求由硬件覆盖更新。

### 9.2 机器Tag

| Tag | 内容 |
| --- | --- |
| `AGE_OF_AGI_SERVER` | 服务器识别标记 |
| `AGE_OF_AGI_SERVER_TRAINING_STATE` | S01任务JSON |
| `AGE_OF_AGI_SERVER_PRODUCTION_STATE` | 生产任务JSON：冻结AI、耗材、工作量、进度及基因快照 |
| `AGE_OF_AGI_SERVER_SELECTED_GENE` / `SELECTED_GENE_SEED` | 所选性状ID及绑定种子身份 |
| `AGE_OF_AGI_SERVER_TRAINING_ACTIVE` | 活动任务/故障保留锁定标记 |
| `AGE_OF_AGI_SERVER_LAST_NIGHT` | 最近主结算的游戏日期 |
| `AGE_OF_AGI_SERVER_CPU_UPGRADE_UNITS` | 已投入电子元件数 |
| `AGE_OF_AGI_SERVER_GPU_UPGRADE_UNITS` | 已投入金属锭数 |
| `AGE_OF_AGI_SERVER_MEMORY_UPGRADE_UNITS` | 已投入金手指数 |
| `AGE_OF_AGI_SERVER_PROCESSING_LOCK` | 当前输入物品由服务器持有的锁定标记 |
| `AGE_OF_AGI_SERVER_PROGRESS_VISIBLE` | 是否展示真实进度 |
| `AGE_OF_AGI_SERVER_PROGRESS_CURRENT` / `PROGRESS_TARGET` | 同前缀的float当前/总工作量 |
| `AGE_OF_AGI_SERVER_WORK_PER_NIGHT` | double有效算力 |
| `AGE_OF_AGI_SERVER_MEMORY_CAPACITY_GB` | long内存总容量 |
| `AGE_OF_AGI_SERVER_STATUS_KEY` | 本地化状态键 |
| `AGE_OF_AGI_SERVER_CPU_UPGRADE_TARGET` / `GPU_UPGRADE_TARGET` | 同前缀的扩建总门槛缓存 |
| `MACHINE_POWER_PER_CYCLE` | 当前硬件汇总能量需求 |

表中合并行的第二个名称沿用相同的 `AGE_OF_AGI_SERVER_` 前缀。显示Tag为刷新缓存，不替代任务JSON中的真实进度。

### 9.3 任务JSON

| 字段 | 含义 |
| --- | --- |
| `Version`、`RecipeId` | 新任务为2、S01；版本1保留旧计费公式 |
| `ModelId` | 模型的唯一身份，使用 `GetUniqueID()` 分配 |
| `TaskType`、`MemoryGb`、`TotalWork`、`ComputeBonus`、`Durability` | 输入模型的冻结属性 |
| `Progress`、`Active`、`WorkNights` | 真实进度、是否进入活动任务、有效工作夜数 |
| TotalData、DataConsumed | 冻结的独立训练数据预算及累计已耗数据 |

有效工作夜数不等于游戏日历天数。运行时的busy、refreshing、cyclePending不写入存档。

读档时先建立窗口与槽位，原版恢复物品Tag和子物品，再由 `onLoaded` 恢复任务、锁定及扩建后的尺寸。旧显示字段不参与校验，恢复后仅保存真实进度、有效夜数与数据费用。

非法JSON或无效任务不会抛出异常中断整个存档加载；服务器记录ModLog警告，隐藏进度并保留活动锁定，暂停资源消耗。没有自动修复或重置异常任务的按钮。

## 10. F9调试

F9 > Spawn 可生成全部登记物品，包括服务器、模型、交换机、四种升级模块、本期耗材和成品。当前 `ModDebugActions.SpawnItem` 针对未训练AI调用：

```csharp
AiModelState.Apply(item, new AiModelAttributes(
    AiTaskTypes.Computing, 1000f, 2, 20f, 5f, 250));
```

六项属性为：计算型、训练量1000、数据需求250、内存2 GB、预测性能+20%、预测初始耐久5。当前数值直接写在该回调中。修改这些参数后重新构建、重启并生成新模型；旧模型不因此改变。

`Spawn ai_trained`另生成内存2GB、性能+20%、初始和当前耐久10的计算型调试成品。生物分析及情报入侵AI通过对应任务头编译、训练获得。普通模型工厂仅建立物品，Compiler负责按组件写入正式模型属性；F9的额外赋值只作用于该调试按钮。缺少完整属性的旧模型仍不能执行任务。

可用 `Skip Night (Machines Only)` 连续测试结算，或正常过夜。验证补电与恢复时，先制造一个无电的夜晚再补电：无论训练是否开始，无电夜都不增加真实进度或有效夜数。

## 11. 构建与检查

从工作区根目录执行：

```powershell
dotnet run --project .\Modding\Tests\ItemCatalogChecks\ItemCatalogChecks.csproj -c Release
dotnet build .\Modding\AgeofAGI\Aoa.csproj -c Release
```

已有依赖恢复结果时可以追加 `--no-restore`。检查项目使用.NET 10；模组目标为 `netstandard2.1`，引用安装目录的游戏、Unity和Harmony程序集，引用依赖不复制到输出。

构建成功会复制DLL、manifest、配置和运行时资源到 `Mods/AgeofAGI/`；重启游戏加载。只生成输出、不部署时使用 `-p:DeployModOnBuild=false`。

### 11.1 自动检查覆盖

覆盖原版材料工厂补丁目标、槽位及存储顺序、分批扩建与窗口重布局、原版材料交互分支保留、硬件求和、CPU加算、耗电、暂停和锁定、完成继承、输出失败清理、重入、模型身份复核、跨夜保存与去重、连续机器调试结算、旧显示字段兼容、交换机槽接受规则、模块升级扣料与累积、tooltip 标注及配方查询接口。

配方和过滤器使用真实原版源码，形状使用真实PNG。场景、库存、UI和Unity API使用替身，训练和生产执行实际代码。自动检查不等同于游戏内布局、声音、材料拖放和真实存档往返验证。

### 11.2 游戏内复核项目

- 标准CPU0＋GPU0的22/夜、4能量/夜符合实际结算；两张GPU0的44/80任务在两轮后完成。
- 无电、补电、输出堵塞及解除堵塞能正确暂停和恢复，锁定和进度符合任务状态。
- 调试结算可以在同一游戏日期连续执行，普通同轮重复回调不重复扣费。
- 材料扩建、旋转GPU放入、各阶段槽位形状及已有任务的保存/读档正常。
- 机器边框、标题、黄色缺电提示、横向布局与实际进度条在实际缩放下可读。

## 12. 已处理的问题与维护要点

| 现象 | 原因与当前处理 |
| --- | --- |
| 机器调试结算只推进一次 | 调试不增加日期；使用早期回调标记新轮次，日期仅辅助拦截重复主回调 |
| 移动内存槽后可能影响旧存档 | 显示排序与存储索引分离，不改变 `CollectStorage` 顺序；但散热器槽的彻底移除破坏了旧九槽存档，读档后物品错位、输出丢失，不做迁移 |
| 扩建后打开中的窗口槽位错位 | `SetShape` 只重布局网格自身；扩容时额外 `contentWindow.Validate()` 重排面板 |

调用原版 `GeneralHelper.IsItemOwned` 前必须自行判空，它不接受null。初始化阶段允许空电池、空模型，不应因创建实例中断读档。继续开发时应先核对 [AgentModdingGuide.md](../../../../AgentModdingGuide.md) 的生命周期与原版API限制。

散热器槽已移除且无迁移；液冷散热器仅注册为电子材料商品，不恢复安装槽。感知分析用途、防火墙／高速连接效果、BTC未完成品获取途径和取消返还仍延期；virus及folder_revol已注册普通商品，专用玩法仍延期。退役的 `serverbox_stage*` 旧物品ID没有自动迁移。

## 13. 成品AI生产

生产与训练共用硬件、八个存储节点、有效夜耗电、锁定、输出预留和每日防重入；生产状态单独保存在 `AGE_OF_AGI_SERVER_PRODUCTION_STATE`。`ServerProduction`负责配方和提交，`ServerProductionState`负责存档校验，`ServerGeneSelector`提供种子性状选择。只有生产成功交付才扣AI耐久1点，训练与暂停不扣；最后一次使用先交付再销毁耗尽AI。

计算型AI将对应普通／稀有BTC未完成品一对一加工为成品；情报入侵AI将空文件夹转为机密情报，要求交换机安装网络访问与包体伪装；生物分析AI结合种子和空复杂数据容器，在基因UI选择一项现有可提取性状后产出一份数据。种子数量、性状等级和热量均保留。工作量、数值与操作说明详见 [Production.md](Production.md)。

复杂数据容器由 `ComplexDataHelper`接入原版DNA盘Tag，写入性状等级1、使用次数4。只拦截该容器的 `PlantGeneticsHelper.UpdateDiskDisplay`以保留外观，其他原版逻辑继续处理应用和耗尽销毁。空／满共用一个物品ID，分别使用virus_empty／dna_data。

`ServerProductionChecks`覆盖多夜推进、单批耐久、最后一次使用、条件暂停、输出拒绝／异常、任务恢复、防重入、升级条件、基因选择、无损种子、原生DNA字段及四次使用。真实Unity选择窗口、原版DNA设备操作和实际存档往返尚未手动验证。
