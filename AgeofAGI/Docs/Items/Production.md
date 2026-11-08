# 成品 AI 生产

2026-11-07：编译模型可携带冻结的架构词条与批处理次数。普通服务器每晚最多尝试B个配方，分布式终端最多N×B个，N为实际分配的供给服务器数；所有尝试共享当晚算力，剩余工作量可推进下一批。电费每晚一次，AI耐久仍按成功交付批次支付。输入、输出或资源受阻时停止后续尝试，保留已提交批次和未完成进度。完整规则见[Compiler.md](Compiler.md)。

代码依据为游戏 0.46D。生产实现位于 [ServerProduction.cs](../../Items/Logic/Server/ServerProduction.cs)，批次状态位于 [ServerProductionState.cs](../../Items/Logic/Server/ServerProductionState.cs)，夜间运行由 [MachineRuntime.cs](../../Items/Logic/Machines/MachineRuntime.cs) 编排。服务器与分布式终端共享配方和事务实现，终端资源调度见 [DistributedTerminal.md](DistributedTerminal.md)。

## 配方与输入

计算型 AI 将普通或稀有 BTC 未完成品加工为对应成品。情报入侵型 AI 将空文件夹加工为机密情报，要求安装网络访问和包体伪装升级。生物分析型 AI 使用种子与空复杂数据容器，通过基因面板选择种子现有、已解锁且可提取的一项性状，生成原生 DNA 数据。

工作量、阈值与配方参数由 [server.csv](../../Data/Definitions/server.csv) 和 [encrypted_items.csv](../../Data/Definitions/encrypted_items.csv) 提供；物品初始价格仍只来自 [items.csv](../../Data/items.csv)。不应在 Helper、运行类或文档中建立第二套配置常量。

基因选择由 [ServerGeneSelector.cs](../../Items/Logic/Server/ServerGeneSelector.cs) 绑定种子身份。提取不改变种子数量、性状等级或热量；[ComplexDataState.cs](../../Items/Logic/ComplexData/ComplexDataState.cs) 沿用原生 DNA 字段，写入 rank 1、初始 4 次使用。显示补丁只处理模组的复杂数据容器；原生使用流程负责销毁耗尽容器。

原始数据载体的抽取使用独立批次条件，不经过普通输出容量判断，详见 [DataExtraction.md](DataExtraction.md)。训练数据费用只适用于未训练模型的训练，不能加入成品 AI 生产。

## 状态与提交

生产冻结模型和来源身份、工作量、进度及基因快照，使用 `AGE_OF_AGI_SERVER_PRODUCTION_STATE` 保存。任务执行期间复用原有输入锁和夜间日标记。普通生产先检查输出并保留待交付物品，再复核输入、硬件和资源条件，通过原生 CraftingRecipe 提交；失败路径回收未提交产物。

有效生产工作量为硬件基础算力乘以当前成品 AI 倍率。只在成功完成并交付一个批次时消耗一次耐久；单纯推进、暂停和失败交付不扣耐久。最后一次使用先交付，再销毁模型。训练保留预测耐久，不支付生产耐久。

## 验证与边界

独立行为检查覆盖配方权限、进度与身份冻结、输入锁、输出失败、资源和耐久守恒、最后一次交付、基因选择及当前格式恢复。目录调整保持这些行为。真实游戏中的基因面板、原生 DNA 设备交互和保存/加载仍需手动验证。

服务器训练曲线模拟与显示保持永久移除；未实现的任务头生产和其他延期功能不因结构整理而启用。当前项目边界以 [AGENTS.md](../../../../AGENTS.md) 为准。
