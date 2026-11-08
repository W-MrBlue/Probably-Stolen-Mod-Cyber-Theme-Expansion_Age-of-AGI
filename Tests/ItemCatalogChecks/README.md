# 测试约定

从游戏安装目录运行默认测试（只使用独立样例，不读取当前可自定义配置）：

```powershell
dotnet run --project .\Modding\Tests\ItemCatalogChecks\ItemCatalogChecks.csproj -c Release
```

实际模组配置校验保留为独立的显式诊断命令，仅在用户要求检查配置时使用，不属于默认测试或开发通过条件：

```powershell
dotnet run --project .\Modding\Tests\ItemCatalogChecks\ItemCatalogChecks.csproj -c Release -- Modding/AgeofAGI --config-only
```

测试用例不得读取或依赖实际模组目录中可自定义的Data、Localization、美术及部署配置。价格、类型、物品清单、容量、属性、耗电、工时、概率、数量或价值帽的调整不应影响默认测试结果，也不应要求同步修改测试样例。解析边界、引用完整性和非法输入用独立样例验证。

`ConfigurationValidator`使用真实解析器和注册器验证传入目录的格式及引用、CSV到实例的一致性，并通过内存覆盖另一组样例数值检查读取一致性。默认测试只将独立样例目录传给它；`--config-only`才会显式传入实际模组目录。实际配置诊断失败不表示默认单元／行为测试失败。

所有默认检查统一在 `BehaviorTestFixture` 创建的临时目录中运行。`Fixtures/Behavior/` 中的 CSV 是独立测试输入，用于验证舍入、配对、累计扣费、跨夜完成、当前格式状态恢复、失败守恒等具体行为，**不是模组默认配置的快照校验或设计规范**。测试可以对显式样例输入断言精确结果；修改实际配置时不要同步这组样例。改变规则实现时才调整有关样例及预期。

行为样例的图片由 `sprites.csv` 的测试形状生成，使用简单白色占格图，不依赖实际美术尺寸、像素或 NPC 素材目录。安装游戏程序集从独立路径定位，临时目录在成功或异常退出时清理。无需复制真实素材或制作特殊目录才能运行完整测试。

当前开发阶段无需考虑旧存档兼容性。新增功能不要求迁移旧字段、保留旧物品ID或适配旧版本状态；当前版本内的保存／加载及状态正确性仍需检查。已有兼容用例不构成后续改动的兼容约束。

`--market-only`只运行市场相关行为样例；`--behavior-only`与默认测试相同，无需再传此参数。两者均不检查实际模组配置：

```powershell
dotnet run --project .\Modding\Tests\ItemCatalogChecks\ItemCatalogChecks.csproj -c Release --no-restore -- Modding/AgeofAGI --behavior-only
```

加密物品检查覆盖四列配方校验、非法ID及任务头、工作量调参、动态输入／产物映射、版本2任务冻结、版本1BTC进度延续和完成后的单次AI耐久扣除。载体表使用Definitions/data_carriers.csv。新增配方表随其他参数每批注册加载一次，工厂配置读取次数为14。

电子市场事件检查覆盖独立类型价格修正、重复注册与禁用清理、成品／未完成BTC共同收购、Astar全量涨价货单、硬件与零件采购、实际BTC成交后解锁食物、全部卖家整包批发及容器数据比例冻结。测试使用独立事件和货单样例，不读取实际事件出售表。
