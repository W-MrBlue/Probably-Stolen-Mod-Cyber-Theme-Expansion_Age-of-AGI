# 电子市场事件

基线游戏0.46D。五组事件注册到原版 `StoreEventManager.normalEventBlueprints`，权重均为15，持续2天，使用默认3天冷却。每个事件在每天开门生成顾客时，从自己的客人列表中等概率抽取1名；不占普通随机顾客名额。事件、动作和待来访客人在模组禁用时按委托身份及来源事件清理。

## 事件与价格

| 事件ID | 价格修正 | 每日客池 |
| --- | --- | --- |
| `age_of_agi.crypto_boom` | `gpu +35%`、`crypto +50%` | 狂热虚拟币投资者、二手投机客、游手好闲的上层人 |
| `age_of_agi.astar_breakthrough` | `electronics +40%`、`data_container +40%` | Astar的狂热拥趸、冷静的投资人、绝望的下层人 |
| `age_of_agi.electronics_surge` | `electronics +25%` | 电子产品爱好者、维修店店主、囤积电子产品的上层商 |
| `age_of_agi.crypto_crash` | `crypto -30%` | 绝望／愤怒的虚拟币投资者 |
| `age_of_agi.ai_fear` | `electronics -30%`、`data_container -20%` | 愤怒的下层居民、数据商人、Astar研究员 |

数据容器只具有`data_container`类型，不再附带`electronics`，所以上述两条修正不会在容器上重复累加。GPU使用独立`gpu`分类，中英文均显示GPU。成品和未完成BTC均具有`crypto`分类，中文为“加密货币”、英文为“Crypto”；未完成品另保留`encrypted_carrier`，中文显示“已加密”、英文显示“Encrypted”。

## 收购规则

- 狂热虚拟币投资者：仅`crypto`，成品与未完成BTC同等接受。
- 二手投机客、游手好闲的上层人：`crypto`或真实GPU。
- Astar事件三名买家：现有综合电子收购范围再加有效数据容器，覆盖事件全部涨价内容；AI模型和代码组件不受影响，仍拒收。
- 电子产品爱好者、囤货上层商：现有综合电子收购范围，不含数据容器、AI模型和代码组件。
- 维修店店主：`electronic_hardware`与`electronic_parts`。

所有规则继续尊重`NOT_FOR_RESALE`。买家分别从原版下层受伤星际客、中层诊所护士、上层医院采购模板创建，保留对应阵营、预算框架和原生交易行为，再将本次预算乘1.75。对白仍通过`client_dialogues.csv`配置，预算句显示放大后的实际余额。

## 出售货单

静态货单在 [Data/electronics_event_stock.csv](../../Data/electronics_event_stock.csv) 中按`seller,item_id,count,data_fraction`配置。`count`展开为独立交付项；`data_fraction=-1`保留工厂初始状态，0～1仅用于原始载体／数据容器并按容量四舍五入冻结数据量。每个卖家必须至少有一行，同一卖家不能重复配置同一物品。全部卖家启用原生整包批发，随机折扣15%～25%，无论对白是否主动说明。

| 卖家 | 货单 |
| --- | --- |
| 绝望的虚拟币投资者（下层） | 未完成普通BTC×2；实际卖出至少1个未完成普通BTC后，本次来访解锁食物收购 |
| 愤怒的虚拟币投资者（上层） | 未完成普通BTC×4、未完成稀有BTC×2 |
| 愤怒的下层居民 | 废旧软盘×2、失效访问卡×1、电容×2、普通PCB×1、GPU0×1 |
| 数据商人 | 音频采样器×1、75%破旧普通数据盘×2、50%普通数据容器×2、50%高容量数据容器×1 |
| Astar研究员 | GPU2×1、CPU1×1、内存2×2、100%普通数据容器×1、50%高容量数据容器×1 |

食物解锁使用`onItemSold`实际成交回调，不把放上柜台视为购买；状态只存在于当前客人工厂闭包，不跨来访共享。静态卖家供货绑定完整对白的末段并防重复交付，GPU按真实属性标注。

## 代码组织

- `ElectronicsEvents.cs`：事件定义、注册、每日抽取及清理。
- `ElectronicsEventClients.cs`：客人工厂、收购predicate、预算、批发和条件食物交易。
- `ElectronicsEventStockCatalog.cs`：出售表解析、引用／数量／数据比例校验与冻结货单展开。
- `ElectronicsEventChecks.cs`（测试项目）：独立事件类型修正、注册清理、收购规则、条件成交与数据比例测试，不读取实际可调货单。
