# 物品功能与代码结构

本目录集中存放物品功能文档。物品源码位于 [Items](../../Items/README.md)，API 与生命周期依据为根目录 [AgentModdingGuide.md](../../../../AgentModdingGuide.md)，本次整理基于游戏 0.46D。

| 目录 | 职责 |
| --- | --- |
| Items 根目录 | 通用物品注册、工厂、映射模型、参数、价值、类别、音效、形状和提示框接入 |
| [Catalogs](../../Items/Catalogs/) | 配置加载、CSV 解析和跨表映射验证 |
| [Definitions](../../Items/Definitions/) | 配置对象、构造参数验证与共享任务类型 |
| [Helpers](../../Items/Helpers/) | 每个物品家族唯一的初始化、回调绑定和生命周期入口 |
| [Logic](../../Items/Logic/) | 按物品家族组织的计算、状态、查询、UI、交易执行与原生补丁 |

Helper 可以编排初始化并转交运行对象；具体执行代码不得重新堆入初始化局部函数。Logic 不引用物品 Helper，跨功能查询使用对应的 State/Runtime/Logic 类。多个物品 ID 可以共享一个功能目录；只需音效或简单标签的物品不额外创建空目录。

`CompilerRuntime`、`DiskWriterRuntime` 和 `DataCollectorRuntime` 使用每台机器独立的字段和方法维护运行过程。Helper 调用 Bind，并绑定 OnLoaded 与初始刷新。服务器与分布式终端分别由自身 Helper 创建 `Logic/Machines/MachineRuntime`，模式由 `MachineWorkMode` 指定。生产、硬件计算、训练状态、升级和显示继续各自分离。

编译器的组件快照只参与布局计算和事务重验。模型写入接收 `AiModelAttributes`，周商和调试生成也使用同一个属性对象，不需要构造虚假的编译网格结果。存档中的 Tag、JSON 字段、库存索引及支付/交付顺序不因源码移动而改变。

| 功能 | 文档 |
| --- | --- |
| 编译器与属性计算 | [Compiler.md](Compiler.md) |
| 服务器与硬件升级 | [Server.md](Server.md) |
| 缸中之脑与每日维生 | [BrainServer.md](BrainServer.md) |
| 模型训练与数据费用 | [Training.md](Training.md) |
| 成品 AI 生产与基因数据 | [Production.md](Production.md) |
| 数据载体抽取 | [DataExtraction.md](DataExtraction.md) |
| 磁盘盒与代码存储器 | [ElectronicStorage.md](ElectronicStorage.md) |
| 分布式终端、交换机和共享存储 | [DistributedTerminal.md](DistributedTerminal.md) |
| GPU 组件、标签与鉴定 | [Gpu.md](Gpu.md) |
| 原版公司字段与新增公司 | [Manufacturers.md](Manufacturers.md) |

测试项目通过 `Items/**/*.cs` 纳入物品源码，新增功能目录无需单独维护文件通配符。默认检查只使用独立行为样例；实际自定义配置诊断仍需显式选择。构建与检查命令见 [AGENTS.md](../../../../AGENTS.md)。真实游戏的 UI、启停及读档仍需在可丢弃的当前构建测试存档中验证。
