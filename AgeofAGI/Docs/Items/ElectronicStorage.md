# 磁盘盒与代码存储器

2026-11-08，原版实现核对基线为游戏0.46D。两件物品均使用现有素材和原生可打开库存窗口，分别只有一个存储节点（索引0）。

| 物品 | 展示类别 | 本体价值 | 内部库存与内容 |
| --- | --- | ---: | --- |
| `age_of_agi.disk_box` 一盒磁盘 | `electronics` 电子产品 | 10 | 8×8格，新建时装入16个真实的 `disk_empty`，只允许放回空白磁盘；描述为空 |
| `age_of_agi.code_container` 代码存储器 | 原生 `STORAGE` 储藏 | 200 | 17×10格，与原版扩容储藏区相同；初始为空，接受 `codeparts` 及本Mod已训练／未训练AI模型 |

价格仅在 `Data/items.csv` 初始化。原版 `GameItem.GetCurrentChildValue` 已累计实际子物品价值，磁盘盒总价为盒价加当前磁盘价值；取出、放回或恢复不同实例价格都会自动反映到总价，不额外复制到 `lateUnitValue`，避免重复估价。代码存储器同样沿用原生储藏物品的内容估价。

容器使用 `DirectoryUtils.CreateInventoryWindow` 和 `ContainerHelper.InitContainerItem`，保留原版所有权及嵌套限制：不能把未购买物品放进自有容器，也不能把自有物品放进未购买容器。磁盘盒带原生 `STORABLE_PACKAGE_TAG`，可放入原生储藏区。源芯片的展示类别仍为空，不额外获得 `codeparts` 分类。

磁盘盒的新建填充通过原生 `UncheckedAccept` 放置四行四列的2×2磁盘，不为预装未购买商品放宽玩家的插入限制。中途失败销毁全部已创建磁盘，再由公共工厂销毁未完成盒体。`SaveManager.IsDecoding` 时工厂不生成内容；读档由原生子节点恢复，`onLoaded` 仅恢复容器／包裹标记，空盒或部分取用的盒子不会补满，不改写已保存本体和磁盘的价格。

夜班的新供货表每次出售1盒磁盘（16盘），替代原来的6个散装空白磁盘。开通礼包仍保留4个散装磁盘。待来访货单继续使用原来的冻结机制。

代码存储器仅在玩家已解锁二级电子产业（`night_shift_service`）且未持有任何代码存储器时，100%加入杰克逊供货单，每张1件，不额外掷概率。供货时通过原生 `PlayerStore.FindAllItem()` 查询当前所有权，涵盖柜台、库存、橱窗及嵌套存储中的物品，不只依赖原生事件传入的早期快照。已拥有或只解锁一级时不刷新；不主动撤下已经摆出的未购买商品。所有原有供货路径使用同一检查：

- 普通商店、上门常规访问和特殊库存：订阅原生 `OnPlaceInventorInventoryItemLate`。
- 初次见面、模块介绍、储藏区访问：限定Harmony Postfix在对应工厂生成后，为原生开场对话尾行追加供货回调，保留原有回调及链头。

供货检查柜台上尚未购买的同ID物品，避免同一次库存重复追加；对话访问还保存本次已经供货的闭包状态。沿用原生付费上架、柜台空间、折扣和交易处理。停用时移除事件并只恢复仍由本Mod持有的对话回调；菜单存在性检查使用 `PlayerStore.HasInstance`。

代码职责：`ElectronicStorageHelper` 负责初始化和加载绑定；`Logic/ElectronicStorage/ElectronicStorageRuntime` 负责库存及装盘；`Npcs/JacksonCodeStorageStock` 负责原生供货接入。F9通过通用物品目录生成两件物品。

Release构建及默认独立行为检查已通过。测试使用独立样例价格、供货数量和生成素材，并直接编入原版 `ContainerHelper` 验证过滤规则；不读取实际可调配置。真实Unity窗口、商店交付与当前格式存档往返仍需重启后在可丢弃测试存档验证。
