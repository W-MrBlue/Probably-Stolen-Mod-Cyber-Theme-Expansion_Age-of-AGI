# 电子商人：电子产业一级

2026-11-08 一级新周货单固定加入1张显卡比色卡（`age_of_agi.gpu_color_checker`），基础价50。它仿照原版香烟色卡激活后打开原生纸张参考面板，展示GPU金手指标准色阶；不消耗物品、不自动修改显卡。供货配置仍在electronics_entry.csv，不额外加入礼包。素材、运行图标和原生交互见[Gpu.md](../../Docs/Items/Gpu.md#显卡比色卡2026-11-08)。

## 代码组织

当前目录按周商、市场、拾荒卖家、灯牌、商品交付、槽位抽取、数量规则分工。2026-11-06新增共用槽位解析与抽取器。

| 文件 | 职责 |
| --- | --- |
| [ElectronicsTrader.cs](ElectronicsTrader.cs) | 一级商人入口、两级共用周商供货、礼包、画像及保存状态 |
| [ElectronicsMarket.cs](ElectronicsMarket.cs) | 市场买卖客人、工厂注册、橱窗分派及市场货池解析 |
| [ElectronicsSalvageSellers.cs](ElectronicsSalvageSellers.cs) | 拾荒者、小毛贼、小偷工厂及对应货池解析 |
| [ElectronicsSignAttraction.cs](ElectronicsSignAttraction.cs) | 灯牌供货、阵营分派和橱窗预测缓存 |
| [ElectronicsStock.cs](ElectronicsStock.cs) | 商品实例创建、原始含量抽取、摆货及MarketOffer |
| [ElectronicsDropCatalog.cs](ElectronicsDropCatalog.cs) | 统一供货商表每批一次加载、槽位概率、槽内均匀抽取、声望门槛及配置校验 |
| [ElectronicsVisitRules.cs](ElectronicsVisitRules.cs) | 随机卖家的可配置尝试件数范围 |
| [ElectronicsEvents.cs](ElectronicsEvents.cs) | 五组市场事件、每日事件客人抽取及注册清理 |
| [ElectronicsEventClients.cs](ElectronicsEventClients.cs) | 事件客人工厂、收购规则、批发与条件食物交易 |
| [ElectronicsEventStockCatalog.cs](ElectronicsEventStockCatalog.cs) | 事件静态出售货单解析与数据比例校验 |

市场与拾荒货池类仍分别命名为 `ElectronicsMarketCatalog`、`ElectronicsSalvageCatalog`，与各自客人工厂位于同一文件。原三个货物工具类统一为 `ElectronicsStock`，无旧类转发层。

2026-11-05新增不诚实电子卖家与不诚实电子拾荒者，各占对应普通分支25%，两者沿用普通货单、件数和原生模板，仅生成所售GPU时通过`GpuLabels`独立虚标：60%向好、30%诚实、10%向坏，幅度为真值的25%；供电接口缺失时70%标为正常。高级卖家与小毛贼／小偷不参与此替换。显卡估值及手动计算器质疑见[显卡鉴定说明](../../Docs/Items/Gpu.md#不诚实卖家价格与计算器)。F9可分别召唤新变体，禁用时清理自己注册的工厂和待来访。

电子产品灯牌已注册为2×2物品，基础值100，在杰克逊处按“尚未拥有则供货”的原版灯牌规则购买。放入橱窗提供75%的电子卖家候选机会；具体规则和素材位置见[Market.md](Market.md#电子产品灯牌)。不改变本页稳定周商的周期。

电子灯牌现已按阵营声望分派电子商人、电子拾荒者、电子小毛贼与电子小偷。随机卖家先抽尝试件数，再逐件选槽并在槽内均匀抽货。小偷另有默认黑市声望60解锁的声望槽；原每访一次稀有骰已由槽位机制替代。配置与完整货单见[StockProbabilities.md](StockProbabilities.md)。柜台失败不补抽。原版普通小偷货单不混入模组电子商品；本页两级稳定周商保持原货单。

2026-11-04，游戏0.46D。王尔德「常规」标签页下的「电子产业」栏：一级500 → 二级追加1200。二级为[夜班](../Nightshift/NightShift.md)，购买后一级继续每7天来访。一级／二级图标分别使用 `Textures/icons/E_service.png`／`E_service_advanced.png`；构建时复制到原生 UI 可加载的 `Textures/Paper/`，不注册为物品。

一级服务ID：age_of_agi.electronics_entry_service；客人ID：age_of_agi.electronics_trader；状态键：electronics_entry_state。配置[Data/electronics_entry.csv](../../Data/electronics_entry.csv)，每个物品注册批次加载一次。

## 起步礼包

一级新购礼包额外赠送1本显卡鉴定指南（`age_of_agi.gpu_guide`），图标来自 `gpu_details/gpu_guide.png`，阅读打开已制作的原生 Paper 书籍。礼包清单购买时冻结，已有冻结礼包不追加。见[显卡指南说明](../../Docs/Items/Gpu.md)。

服务器一台（预装满电电池、半满的30/60普通数据容器），标准CPU（cpu0）、入门GPU（gpu0）、8GB内存各一件，另送下列三个已训练计算AI。服务器的原存储索引不变，容器安装在节点8。2026-11-05将新礼包数据量改为30，容量仍为60，并将新交付服务器的电池按原版最大容量充满；已领取机器不补电，每周售卖的容器仍为60/60。

| 模型 | 内存 | 性能加成 | 耐久 |
| --- | ---: | ---: | ---: |
| 入门成品1 | 2GB | +10% | 3 |
| 入门成品2 | 4GB | +20% | 4 |
| 入门成品3 | 8GB | +30% | 5 |

三个模型均可在赠送硬件上直接工作。无需先购买二级或自行编译。满库存保留礼包序号，后续开店／读档／F9可补领；不补电或重发已领机器。

一级礼包赠送的显卡固定为该型号的标准配置：使用pos.csv中的normal_sprite与坐标，显存、上下金手指、供电接口和供电模块全部正常，不参与随机品质抽取。真实算力及耗电由gpu.csv核心基础值按正常组件计算，初始标签与真实属性一致；当前gpu0为算力10、耗电2。此规则也适用于未领礼包的后续补领，每周售卖和普通来源仍按原概率抽取。已领显卡及其存档不重置。

## 每周固定货单

服务器空机、标准CPU（cpu0）、入门GPU、8GB内存、60/60数据容器、已训练计算AI、未训练计算AI各1件；普通BTC未完成品3件。cpu0当前显示名是「标准CPU」，不加入低级CPU随机商人来源。

| 售卖模型 | 内存 | 性能加成 | 耐久 | 训练量 | 训练数据 | 基础值 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 已训练计算AI | 4GB | +20% | 4 | 无 | 无 | 80 |
| 未训练计算AI | 6GB | +40% | 7 | 120 | 30 | 100 |

未训练模型可直接装入一级服务器训练；无需编译器。cpu0＋gpu0硬件算力22，预计6个有效夜，消耗24电和30数据。训练完成完整继承内存、性能和耐久。已训练模型生产普通BTC预计2个有效夜，不消耗训练数据。耐久按成功批次扣除。

数据容器的60数据总估值为300（空壳60＋数据240），作为可选训练耗材。普通BTC未完成品40、成品120。报价继续使用原版供应商和难度修正。新增随机卖家、分层收购商、电子橱窗和AI动态估值见[电子交易客人族](Market.md)，不提供销量保底。

模型统一沿用ai_trained／ai_untrained两个物品ID及现有图，属性在礼包／待来访货单中冻结；同ID新模型本体价格来自items.csv，旧存档物品价格不改，不增加动态品质定价。

## 随机立绘与保存

每次自然来访从Textures/npcs下maleSci_0、maleSci_1、femaleSci_0、femaleSci_1等概率选一张，名称统一为「电子商人」。选择随待来访货单保存，读档不重抽。SpriteDict补丁只解析本模组精确命名空间键，使用原生像素缩放并在禁用时释放生成Sprite。

当前中文显示名为「退休电子商人」。解锁次日首次到访，交付货单后每7天再次到访；两级周期供货商始终如实标注显卡算力、耗电、供电接口和金手指。周货仍可随机生成真实硬件缺陷，但不会虚标或隐瞒，物理缺陷在初始标签和报价中体现。

原生纹理加载不递归npcs目录，构建通过`ModNpcTexture`将该目录的PNG／JPG复制到输出和部署目录的Textures根目录，生成字节一致的运行副本；源图片保留在npcs目录。显卡供货商和二手设备商也使用此复制规则及各自的专用立绘，见[电子交易客人族](Market.md#专用立绘)。

F9「召唤电子商人（调试供货）」只生成测试会面，不解锁、不发礼包、不改变自然来访；「领取AI服务物品（补领）」分别报告两级领取结果。自然周货、赠品索引和随机立绘跨读档固定；原版临时交易界面仍不持久化。

两级错峰和旧夜班权益兼容见[夜班说明](../Nightshift/NightShift.md)。自动检查通过，实际游戏UI、立绘、报价和存档往返待手动确认。

2026-11-06随机电子卖家改为可配置低／中／高三槽，槽内均匀抽取；小偷另有声望槽。不诚实普通商首位固定GPU1保留。交换机、空文件夹、复杂数据容器从所有当前供货清单移除，夜班周货中的复杂数据容器也删除；两级周商仍用独立供货机制。数据收购已合并为企业计算服务商，不再随访出售空普通容器。

电子设备供货商表统一为 [Data/electronics_suppliers.csv](../../Data/electronics_suppliers.csv)，包括dealer、advanced_dealer、scavenger、petty_thief、thief和gpu_supplier。市场／拾荒目录共享已解析的同一货池实例，保持各自客人工厂、交易行为和买家收购逻辑；两级固定周商继续使用各自配置。旧market／salvage表已删除，构建清理对应输出和部署副本。
