# 剧情与角色

基线：本机游戏 0.46D。`Quorra/` 为葵拉剧情，`Stories/` 为共享执行框架。

电子产业一级的实际购买事件记录购买日，次日将葵拉插入原生客人队首，不打断当前访客。已排队或正在访问时不重复生成；营业结束丢弃的运行队列可在下次营业重建。已有存档中无法得知的历史购买日不回填，可用 F9「召唤未知女孩（队首）」开始或继续未完成剧情。

未知女孩请求藏身 → 玩家选择 `???` → 葵拉完整说完「没时间了；我先躲一下！；等下再说!；」→ 黑屏中隐藏葵拉 → 黑屏结束，治安官按普通客人的原生路径从橱窗左侧走到中间 → 完整问完「她大概长这样，你有看到她去哪儿了吗？」后放置通缉单 → 两次询问。第一次否认的独立停顿和后续问句共用 `npc_quorra_officer_doubt`，所有省略号均保持原文，只由分号分隔气泡，不合成停顿节点，遵循原版打字和推进设置。

- 任一次供出：黑屏换回葵拉并显示两个原生治安官，玻璃下摇；两位角色完成对白后走原生抓捕离场，保存 `arrested`。
- 连续否认：治安官告辞 → 黑屏换回葵拉；自有违禁物品触发玩笑，自有 `serverbox` 触发服务器对白；递卡与介绍后保存 `protected`、`contact_received`，称呼改为葵拉。

通缉单 `age_of_agi.quorra_wanted` 中文显示名为「治安部留下的通缉单」，无描述，占满 2×2；电话卡 `age_of_agi.quorra_number` 显示名为「葵拉的电话卡」，占满 2×1。治安官问话末句的 `AfterText` 回调交付通缉单，保护分支「将一张卡片推到了桌前」节点交付电话卡；供出分支不发电话卡。两者都立即标为自有；通缉单优先交付前台，再尝试后台、仓库、橱窗。电话卡优先放在前柜台（外侧）底部中央；不可放置时，按距该位置的直线距离由近到远搜索同一柜台的原生槽位，等距时优先底部行、再优先左侧。所有候选均通过原生槽位检查，不挪动已有物品；整个前柜台无空位或锁定时保留待交付。成功交付并记录标记后，分别播放物品配置的 `soundDragEnd` 一次；失败或重复领取不播放音效。重新开店、读档或 F9「补领葵拉剧情物品」可重试。已发放即使之后丢弃也不重复发放；保护结果与 `contact_received` 不受暂时缺少存储空间影响。电话卡只提供联系方式详情，没有新增拨号系统。

`Items/Logic/StoryDocuments/QuorraDocuments.cs` 共用事务与查看逻辑，`StoryDocumentHelper` 分别绑定两个物品的创建和读档回调。使用 `Textures/npcs/` 中用户提供的四张 PNG：

| 物品 | 缩略图 | 完整详情 |
| --- | --- | --- |
| 通缉单 | `Quorra_wanted_small.png`（32×32） | `Quorra_wanted.png`（350×450） |
| 电话卡 | `Quorra_number_small.png`（32×16） | `Quorra_number.png`（137×85） |

构建原样复制缩略图到部署的 `Textures/Items`，详情到 `Textures/Paper`，由原生加载器持有纹理与精灵。通缉单继续使用完整原图背景的 PaperUI。电话卡由 `QuorraPhoneCard` 复制原版 `PaperUIManager.JoeWildBusinessCard` 的实际尺寸、比例、拖动与关闭布局，替换为葵拉电话卡原图，停用缩放及乔·王尔德原回调；不再按137×85源像素创建小纸张窗口，也不显示缩放按钮。两者不覆盖额外文字；电话卡使用与原版名片一致的 `carton_card` 音效预设。关闭后可重新打开或再次点击切换；禁用释放自身窗口，保留原版名片与加载器管理的图片。价格与音效预设分别由 `items.csv` 配置。不修改原版通缉列表或添加赏金。

源美术为 `art_src/Quorra_V1.png`，当前运行美术为 `Textures/npcs/Quorra_V1.png`；更名保留两份原有文件内容。构建同步运行美术到部署的 `Textures/Quorra_V1.png`，不注册为物品。精灵保持原生人物像素比例，离开模组时只释放自身生成的精灵及详情窗口。

两个详情窗口的 X 均使用 `Textures/gpu_details/paper_buttons_close3.png`，构建复制到 `Textures/Paper`。通缉单在 PaperUI 的 chrome 定义中指定该图；电话卡为原关闭按钮显式绑定 Image、白色图标和点击目标，避免原卡文字 X 被移除后只剩空白按钮，也避免旧 SpriteSwap 状态把新图替换回去。

## 添加剧情

1. 创建 `StoryDefinition`：唯一 ID、购买事件 `UpgradeId`、等待 `DelayDays`、可选额外 `Condition`、`StartNode` 和原生 `StoreClient` 工厂。在模组入口将定义传给 `StoryDirector.Enable`。框架本身不引用具体角色或物品；后续剧情可通过 `StoryDirector.Read(id).Outcome` / `.Has(flag)` 查询前置结果。
2. 每个 `StoryNode` 给定 `TextKey`；开场用 `UseClientGreeting`，继续从 `Data/client_dialogues.csv` 选择开场。中英文分号拆为多个原生气泡；共享 ClientDialogueText 只按中英文分号分隔气泡，所有独立或句内省略号都保持原文，不展开、不插入停顿节点，适用于夜班等供应商及剧情对白。无判断、角色/场景变化或交付等事件边界的连续对白应收敛为一个 key，以分号区分气泡。`Next` 是顺序边，`Choices` 是选择边，`SelectNext` 在当前块末句开始时按条件连接 `nextDialogue`，普通续接仍由原生手动/自动推进，不使用末句延迟回调或强制启动；当前静态图需无环，循环访问宜拆成独立剧情事件。
3. `PrepareClient` 在排队前准备角色身份，供读档后的原生行走渲染使用，不执行物品交付。`Enter` 设置场景或执行幂等事务，`AfterText` 在整段末句结束后执行；选择分支也会先结算当前末句回调。`Blackout` 让入场准备在原生黑屏中执行，`NormalArrival` 在黑屏结束后走原生普通访客路径，到达后才开始新主对白。停顿不增加独立的打字或显示补丁。`Outcome` 标记终局，可选 `Finish` 接管原生离场方式，默认普通离场。选择动作先提交目标节点及标记，再让原生 UI 继续，避免在选择派发中同步替换 `currentDialog`。
4. 新对白放 `Localization/Npcs/en.csv`、`zh.csv`；物品由目录与拥有它的 Helper 绑定，价格只在 `items.csv`。不要在 `Init` 或没有 `PlayerStore.HasInstance` 时访问存档单例。

`story_states` 保存每个剧情的触发日、当前节点、结束日、结果、布尔标记及字符串值，不保存 Dialogue、Unity 对象或原生客人队列。布尔标记累积为真；另一路补领事务提交的发放标记在活动会话再次保存时仍保留。读档重建当前节点，可能重播该节点的文本，不重复已经提交的物品事务。当前格式存读档有效，不提供历史名称/存档迁移。

现有标记：`shelter_agreed`、`denied_sighting`、`reported`、`contact_received`、`wanted_pending`、`wanted_delivered`、`phone_card_pending`、`phone_card_delivered`；`Values.officer_sprite` 固定询问者立绘。结果值为 `protected` / `arrested`。

## 验证

对白时序：全部文字（包括省略号）由原生逐字显示和手动/自动推进控制；省略号字符等待及句末阅读、选择显示、结束和事件等待均沿用原版。模组在节点开头保存剧情状态、设置角色或交付电话卡；通缉单、结局及黑屏等末句事件通过原生 `endAction`，其默认延迟为 1.1 秒。黑屏沿用原生 0.2 秒淡入、0.25 秒全黑、0.2 秒淡出；治安官走到窗口后，原生再等 0.25 秒开口。普通条件续接没有模组延迟、不调用强制 `StartDialogue`，也不因为省略号额外插入节点。省略号等待覆盖已按用户要求回滚；ClientDialogueText 只保留显式字面 \p 到原生 <waitfor=1> 的转换，不再注册对白时序或打字器补丁。

Release 构建与独立行为检查覆盖次日触发、排队防重、两个供出入口、双重否认、条件对白、两件物品的目录工厂/缩略图/尺寸/音效/查看/满仓补领、独立详情窗口、当前状态重建和禁用清理。默认检查只用独立样例及生成美术，不读取用户当前 PNG 或可调配置。真实游戏内仍须用当前版本的一次性测试存档核验黑屏节奏、双治安官层次、玻璃动画、中文气泡和详情窗口。
