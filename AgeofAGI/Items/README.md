# Item Catalog

## Structure and ownership

Items has four subdirectories: Catalogs, Definitions, Helpers and Logic. Root files provide common registration, creation, metadata, audio/type/shape handling and tooltip integration. CatalogCsvReader lives in Catalogs; DefinitionValidation and shared AiTaskTypes live in Definitions. Feature documentation starts at [Docs/Items](../Docs/Items/README.md).

Helpers contain owning initialization, callback-binding and lifecycle entry points. Each item/family still binds exactly one owner through ItemFactoryDirectory; GpuInspectionHelper remains a separate inspection lifecycle entry. The owning helper applies common MachineParameters/ItemContentValue where needed, invokes the captured audio initializer once and binds load callbacks. Tooltip and cleanup entry methods delegate to Logic.

Logic/<Family> contains concrete calculations, item queries, saved state, UI, native patches and runtime transactions. Its namespaces match the directories and it has no dependency on item Helpers. Families group related IDs; trivial audio/tag-only initializers do not need empty Logic directories. Cross-feature consumers call the relevant state/logic API rather than a Helper.

CompilerRuntime, DiskWriterRuntime and DataCollectorRuntime hold per-machine fields and execution/refresh/load methods. Their callbacks retain the runtime instance. Both ServerHelper and DistributedTerminalHelper create Logic/Machines/MachineRuntime using explicit MachineWorkMode values; ServerState, ServerTooltip and ServerUpgrades separate metadata/queries, display and material interactions. Inventory indices, tags, resource transactions and save schemas retain their existing behavior.

AiModelAttributes carries frozen model properties without compiler components or grid geometry. CompilerAssemblyResult retains component snapshots for transaction validation and exposes Model for AiModelState.Apply; suppliers and debug creation construct AiModelAttributes directly.

Server training-curve simulation/display and its sampling, rendering, noise configuration and preview tools have been permanently removed. Real training progress, data charges, effective-night count, output transactions and storage indices remain.


Distributed terminals and shared storage are documented in [DistributedTerminal.md](../Docs/Items/DistributedTerminal.md). Each contributing server supplies raw hardware compute and memory; only the terminal AI supplies the production multiplier and pays durability on completed batches. High-speed switch interconnection is now implemented.

Server carrier extraction, the appended data container bay, memory-sized batches and dynamic content values are documented in [DataExtraction.md](../Docs/Items/DataExtraction.md). Prices remain CSV data: `items.csv` holds shell values and `data_carriers.csv` holds per-unit raw/parsed data values.

Each registered item has one row in `Data/items.csv`; `sprite_key` selects its initial appearance.

Disk boxes and code storage use the `electronic_storage` kind with one owning ElectronicStorageHelper. Native container windows, prepacked disks and Jackson supply integration are documented in [ElectronicStorage.md](../Docs/Items/ElectronicStorage.md).
The initial names and descriptions are editable placeholders based on filenames.
PNG files inside `Textures/Items/` may also be alternate appearances of the same item.
Registration alone adds no machine behavior, recipes, or loot entries.

## Mapping format

```csv
item_id,kind,sprite_key,base_value,name_key,description_key,comment_key,types,audio_preset,contraband_level,manufacturer
age_of_agi.gpu0,gpu,gpu0,100,item_gpu0_name,item_gpu0_desc,item_gpu0_comment,electronics,module,none,age_of_agi.silicon
```

- Keep item IDs stable after release. Prefix them with `age_of_agi.`.
- Sprite keys are filenames without `.png`; do not include a directory.
- `kind` selects initialization; it does not set a game's displayed item category. An empty kind becomes `generic`. Kinds are normalized to lowercase. `gpu`, `cpu`, `memory`, `base_model`, `ai_parts`, `ai_parts_chip`, `ai_model`, and configured `machine` rows add specialized initialization; other kinds keep the common item behavior.
- Common mapping keys are unquoted identifiers; description_key and comment_key may be blank. base_value is a required non-negative Int64. `types` contains semicolon-separated category IDs; an empty cell means no categories. Blank lines and `#` comments are allowed.
- Item IDs must be unique. Different item variants may share a sprite key. Missing PNGs and malformed rows reject catalog loading with a line-numbered error.
- Add matching name and any non-empty description/comment keys to both `Localization/en.csv` and `Localization/zh.csv` (UTF-8, no header).
- Read the catalog and register debug buttons in `OnModItemDirectoryInit`. On game 0.46D, `ReadModFileText` cannot find the mod during `Init`; do not move file reads into `Init` or `OnEnable`.
- Localization resolves in the factory: current language, then English, then the key. Do not resolve translations during `Init` or `OnEnable`.

## Shared CSV parsing

The optional `manufacturer` value in column eleven initializes the native company Tag through `GameItem.EnableManufacturer`. Native IDs such as `solana` retain native names; the three mod companies use a scoped `DisplayManufacturer` patch. Blank cells leave the field unset, and load callbacks preserve saved instance ownership. See [Manufacturers.md](../Docs/Items/Manufacturers.md).

All configuration under Data uses CSV. Item mapping stays in Data/items.csv; category tables and data_carriers.csv live in Data/Definitions; Night Shift stock stays in Data/night_shift.csv. See [Data/README.md](../Data/README.md) for table conventions.

Items/Catalogs/CatalogCsvReader.cs reads configuration once during directory registration. It validates required headers, rejects unknown or duplicate columns, checks row widths and case-insensitive duplicate definition IDs, parses invariant Int32/finite-float values, and reports file/line/field errors. Tables use unquoted fields, optional BOM, blank lines and # comments. Columns can be reordered by name; row order is preserved. ReadItemId reuses DefinitionValidation. Definition constructors retain gameplay range checks and catalogs retain item mapping and recipe constraints.

Each Catalog declares its columns and constructs its own Definition:

```csharp
var definitions = CatalogCsvReader.ReadDefinitions(csv, CatalogPath, "CPU",
    new[] { "id", "powerBoostRate", "powerPerCycle" },
    (entry, id) => new CpuDefinition(id, entry.ReadFloat("powerBoostRate"), entry.ReadInt("powerPerCycle")));
```

To add a field, extend the table header, catalog columns, callback and Definition. Saved training, production and supplier state retain their existing JSON serialization.

## Common base value

Constructor guards are shared in `Items/Definitions/DefinitionValidation.cs`: `RequireItemId` checks the mod prefix and allowed ID characters; `RequirePositive`, `RequireFinitePositive`, and `RequireNonNegative` check numeric ranges. CPU/GPU/memory/machine Definitions choose the appropriate guard for each field, and `ItemDefinition` uses the Int64 non-negative guard for prices. Callers supply `nameof(parameter)` and a descriptive label so exceptions retain field and family context. This is a static utility; CSV structure, duplicate IDs, and CSV mappings remain the catalogs' responsibility.

Every item has a required `base_value` column immediately after `sprite_key`, including generic items, hardware, and draft machines. Use plain decimal digits in the range 0–9223372036854775807; zero is allowed, while empty cells, signs, fractional/exponential notation, and overflow are rejected with a CSV row error. Parsing uses invariant culture.

`ItemDefinition.BaseValue` owns this common value. `CatalogItemFactory` calls `item.SetValue(definition.BaseValue)` once before type-specific initialization. GPU/CPU/memory/machine Definitions, CSV catalogs, and Helpers no longer store or apply prices; their strict CSV parsers reject legacy `baseValue` columns to avoid two conflicting price sources. The existing configured prices were preserved during migration, and previously unpriced items now explicitly contain zero. Saved instance prices still follow native save restoration; changing CSV changes newly created items and does not rewrite existing saves.

## Optional description and comment

Keep all eleven columns in every CSV row. `description_key` and `comment_key` can be empty or whitespace; non-empty keys must be valid identifiers. Empty keys skip localization lookup and assign `""` to the native `shortDescription` and `flavorText` fields respectively. Names remain required. The native tooltip omits empty text, renders descriptions normally, and renders comments after them in gray italics.

`comment_key` is inserted between `description_key` and `types`. Each existing item has an `item_*_comment` key. In both language files, its entry is placed directly after the same item's description; comment text starts empty for authors to fill in:

```csv
item_gpu0_name,GPU 0
item_gpu0_desc,A graphics processor component for AI hardware.
item_gpu0_comment,
```

Language values can also be empty. An explicitly empty current-language value suppresses that text; a missing key falls back to English, then the key itself with the game's warning. No custom tooltip hook is needed for either field. Clearing an optional key does not delete its localization entry.

## Per-item audio presets

The required ninth `audio_preset` column selects native audio initialization for each item. `Items/ItemAudioPresets.cs` owns a private dictionary of lowercase keys to `Action<GameItem>` method groups. `ItemCatalog` validates keys with a CSV row diagnostic; `ItemFactoryDirectory` resolves and captures delegates when registering factories. Each owning Helper invokes its captured delegate once. Generic items use `GenericItemHelper`. Creation performs no audio lookup or item-ID dispatch, and `CatalogItemFactory` remains responsible only for common metadata.

Keys are case-sensitive, with surrounding whitespace trimmed. Use `none` to leave native defaults untouched; blank or unknown names are errors. The initial migration preserves existing presets, including `gpu0=module`, other GPUs `small_machine`, `serverbox=metal_machine`, `data_container=battery` and `disk_empty=none`. Helpers contain no hardcoded fallback audio calls that could overwrite the selected preset.

Available keys: `none`, `default`, `battery`, `module`, `small_module`, `small_machine`, `metal_machine`, `tiny_item`, `tiny_metal`, `paper_sheet`, `plastic_card`, `plastic_container`, `solid_metal`, `glass`, `book`, `cassette`, `scanner`, `scrap_metal`, `carton_box`, `carton_card`, `dossier`. Add an explicit dictionary entry to expose another verified native method; do not use reflection. Some methods also assign container or activation sounds. `dossier` only sets open/close sounds and does not assign pickup/drop sounds. Initializers assign fields; actual playback remains native.

## State sprites and merged items

Compiler states share `age_of_agi.compiler` (initial sprite `compiler_idle`); server chassis stages share `age_of_agi.serverbox` (initial sprite `serverbox_stage0`). Both retain `kind=machine`. The old `compiler_*` and `serverbox_stage*` item IDs are no longer registered or offered by F9 Spawn; their PNG files remain deployed as artwork. Disk writer states also share one ID, `age_of_agi.disk_writer`. There are currently 82 registered items and 100 item-folder PNGs, including 16 state sprites, the GPU inspection sprite and Night Shift's portrait copy. `virus` and `folder_revol` are registered data items; their special gameplay remains deferred. Task heads have individual sprites. The five template items have been removed. Old saves referencing retired IDs or the old disk-writer storage layout have no migration; use a test save and spawn the canonical IDs.

Raw data carriers use matching `age_of_agi.dc_*` IDs and `dc_*.png` filenames. The original six carriers have been renamed without compatibility aliases; `data_container` and `hypo_data_container` keep their identities and six state sprites. Four additional carriers use `DataContentHelper` and the shared CSV capacity/value configuration. Nine electronic materials use the owning `ElectronicMaterialHelper`, `kind=electronic_material`, and category `electronics`; they only receive native movement sounds. Their prices and the new carrier capacities are initial development values. Registration exposes F9 spawning without adding supply, crafting recipes or hardware installation behavior.

The alternate assets are `disk_writer_empty`, `data_collector1`, `compiler_working` and `serverbox_stage1`, `serverbox_stage2` and `dna_data`; they do not require their own CSV rows or item-name translations. Item ID and display name stay stable when appearance changes. `CompilerHelper` switches the compiler sprites according to recipe readiness. `DiskWriterHelper` uses `disk_writer_empty` when the blank-disk input slot is empty and `disk_writer` otherwise, including after consuming the last disk. `DataCollectorHelper` uses `data_collector1` while at least one existing data container is installed and `data_collector0` when empty; expansion does not select the sprite. `ServerHelper` uses stage 0 for an empty GPU bay, stage 1 for installed items occupying at most 12 cells, and stage 2 for more than 12 occupied cells in total. Count actual shape cells, excluding transparent holes; bay capacity and GPU count alone do not select stage 2. These machines refresh on inventory changes and load.

Compiler sprites are both 48×48; disk writer sprites are both 32×48. Server stages 0–2 are 64×80 (4×5 cells at grid size 16) and share a footprint; all item sprite dimensions must be positive multiples of 16. Stages 3–4 remain reserved; stage 4 is 80×128 (5×8 cells). Do not switch stage 4 as a same-size visual variant without deciding and validating the footprint change. Disk writer and server state changes use `SetSprite(ModConstants.ModId, key)` to preserve occupied shape, position, rotation and reflection; `SetSpriteAndShapeFromMod` recomputes the shape.

## Displayed categories

`kind` selects initialization; `types` controls the native category row. Use lowercase IDs. Electronic merchandise uses `electronics` with optional subtypes: `electronic_parts` for physical parts, `electronic_hardware` for functional hardware, `gpu` for GPUs, `crypto` for both finished and unfinished BTC, `data_carrier` for raw data and `encrypted_carrier` for encrypted recipe inputs. Parsed-data containers use only `data_container`, without `electronics`, so event modifiers cannot apply twice. Ordinary electronics need no subtype. AI models use only `ai_models`; code components and base models use only `codeparts`; machines use native `machine`. For multiple categories, write `electronics;electronic_hardware` in column eight, followed by the audio preset in column nine. Parsing trims, converts to invariant lowercase, and removes duplicates in order. Leading/trailing semicolons, empty entries, quotes, and invalid identifiers are rejected with the CSV line number. Empty categories retain an empty eighth cell, for example `...,item_disk_empty_comment,,none,none`.

`ItemDefinition.Types` holds a copied, read-only list. `CatalogItemFactory` calls `ItemTypeHelper.ApplyType` for each entry, independently of the type-specific Helper. It restores recognized native IDs to uppercase (`machine` → `MACHINE`, `module` → `MODULE`) before `SetGameItemType`, preserving native category lists and tags. Custom IDs remain lowercase. The native row handles positioning and brackets. `SetType` alone does not populate that row.

`Items/ItemTypeHelper.cs` uses one Harmony prefix with no per-category mapping dictionary. For custom categories, the lowercase ID is the localization key itself: `electronics,电子产品` and `codeparts,代码组件` in zh.csv, with matching English entries in en.csv. Add new custom IDs to CSV and both language files without changing C# code. Lookup uses the current locale and English fallback; absent keys, native categories, unregistered mods, and disabled mods pass through to the original method. Native categories keep the game's Mechanic translations (`MACHINE` → `type_machine`); adding a Mod key named `machine` does not replace them. The loader installs the patch automatically. This does not add custom IDs to global native filter lists.

## Per-item contraband levels

The required tenth `contraband_level` column accepts `none`, `low`, `mid`, `high`, or `critical`. Keys are lowercase and case-sensitive; surrounding whitespace is trimmed. Blank or unknown values report a CSV row error. `Items/ItemContrabandLevels.cs` maps these keys to native severities 0–4, validated by `ItemDefinition` before factories run. `CatalogItemFactory` calls native `ContrabandHelper.RemoveContrabandStatus` for `none`, or `InitContrabandItem(item, level)` for levels 1–4. The native API sets the category, contraband flag, and both string and integer severity fields, so native inspection and tooltips use the configured status. Set the status through this column; no extra `contraband` type entry is needed.

All existing rows initially use `none`. Configuration initializes new items; native save restoration retains saved instance status without reapplying the initial level on load. Verified against game 0.46D.

## Registration lifecycle

Factory selection follows the native directory pattern: `ItemRegistration` asks `Items/ItemFactoryDirectory.cs` to build an `item ID → Func<GameItem>` plan before touching the live directory. Its `RegisterGpus/RegisterCpus/RegisterMemory/RegisterMachines/RegisterAiParts/RegisterDiskWriter` methods select and validate each family once. Hardware callbacks capture their immutable Definition; machine callbacks also capture the recipe catalog they need. Unknown and generic kinds receive the common factory alone.

`CatalogItemFactory.Create(definition, initialize)` handles shared price/text/sprite/source/categories/contraband status, then invokes the already-bound initializer. It contains no kind or item-ID dispatch and takes no category catalogs. Each ID binds one owning helper; Add rejects a second initializer. Machine helpers apply MachineParameters internally before constructing their UI/recipes. DataContentHelper initializes shared ItemContentValue internally. Exceptions stop subsequent steps and destroy the incomplete item. Registration creates no items and resolves no localized text; machine callbacks consult captured catalogs without reloading files.

To add another family, add a registration method and call it from `ItemFactoryDirectory.CreateFactories`; use `Add(row.ItemId, (item, audio) => YourHelper.Init(item, capturedConfig, audio))`. Keep all catalog loads/validation in that registration phase. There is no need to extend `CatalogItemFactory` with another branch, change existing CSV formats, or add reflection-based automatic Tag dispatch.

`Init` creates the manifest-bound registration service without reading files or scene state. `OnEnable` subscribes once to `OnModItemDirectoryInit`. The callback reads the CSV and validates the complete batch before changing the directory:

- Every sprite must exist in the enabled mod's loaded `Sprites` dictionary; a file merely existing on disk is insufficient.
- Item IDs must not belong to another local or global factory. The game's `Directory.Add` removes an existing global match, so collision checks must run before calling it.
- Every `Add` return value is checked. If an add fails, factories added by that batch are removed by delegate identity.
- A successful repeat registration replaces this service's previous factories. `OnDisable` removes only factories that still reference this service's delegates; replacements made later by other code are preserved.

`CatalogItemFactory` resolves localized text, delegates sprite/shape construction to the game, and explicitly sets the source mod ID/name on each fresh instance. It checks the actual `UISettings.current.gridSize` when creating an item, after scene state exists. Sprite width/height must each be at least one cell and exact multiples of that size. It preserves transparent padding and uses the game's center-alpha occupancy calculation.

Missing assets and ID conflicts fail before changing registrations. If a repeat batch fails during the add phase, the previous batch has already been removed and the incomplete replacement is cleaned up; correct the error and restart. Hook failures are reported in F8 and do not automatically mark the entire mod disabled.

## AI parts family

Shared tooltip display lives in `Items/ItemTooltipHelper.cs`. The early tooltip hook restores the native estimated/base-value row when the current estimate is zero, using the game's localization key and price color. It checks the live native value (including contents and modifiers), so nonzero price rows are not duplicated. Prices and save data are never changed for display.

The scoped `GameItemElement.GetTooltipBasic` transpiler wraps its category call and header spacer: an empty native type list renders neither the category row nor its spacer. Typed items keep the native header. Both changes require this mod's `MOD_ID_TAG` and an enabled loader entry; native/foreign items pass through. The transpiler verifies its two call sites against game 0.46D and rejects unsupported layouts. The loader applies it automatically.

`ItemFactoryDirectory.RegisterAiParts` binds both `kind=ai_parts` and `kind=ai_parts_chip` to `Helpers/AiPartsHelper.cs` at registration time. The current family contains 16 code components (five decoder shapes, five encoder shapes, feedback, link, regularization and three task heads) and two source chips. All receive the current `AiPartState.FamilyTag`; components additionally receive `PartTag`, while chips receive `ChipTag`.

Use `AiPartState.IsPart` or inspect the corresponding Tag to query membership. Regular component CSV rows set `types=codeparts`, displayed as “代码组件” / “Code Components” through the same-name language key. Chip CSV rows leave `types` empty, so their logical family membership does not add a displayed category. `ai_parts.csv` supplies basis attributes and functional/task-head roles; common prices and text remain owned by the shared factory. Feedback retains its native 4×1 shape.

Fresh disk-writer products run through the same component initializer, and retained source chips use the chip initializer. The machine's exact chip-ID whitelist and ten recipes remain unchanged; adding a family member does not implicitly add a new disk-writer recipe. Existing saved objects retain their saved metadata; spawn or print fresh components when verifying new categories.

## GPU definitions

`Definitions/GpuDefinition.cs` is an immutable configuration model. `Catalogs/GpuCatalog.cs` reads `Data/Definitions/gpu.csv` once per registration batch containing GPU items. It validates the entire definition table and its mapping to `kind=gpu` rows before any directory changes. Factories capture that catalog snapshot, so creating items does not read configuration again.

Each CSV row requires:

| Field | Type and rule |
| --- | --- |
| `id` | Exact CSV item ID, prefixed by `age_of_agi.` |
| `computePower` | Positive 32-bit integer |
| `powerPerCycle` | Non-negative 32-bit integer; zero is allowed |

Unknown or duplicate columns, duplicate IDs, missing columns, fractional integers, quoted fields and out-of-range values are rejected. GPU definitions and kind=gpu item rows must match in both directions.

`Helpers/GpuHelper.InitGpu(item, definition)` sets native GPU movement sounds and tags `AGE_OF_AGI_GPU`, `AGE_OF_AGI_COMPUTE_POWER`, and `AGE_OF_AGI_POWER_PER_CYCLE`. It rejects an item whose ID differs from the definition. Other code can use `IsGpu`, `GetComputePower`, and `GetPowerPerCycle` to query these values.

`GpuRuntime.CreateTooltip` uses `OnCreateTooltipLate` to show compute power and power per cycle before the description. It reads the item's current tags and resolves `gpu_stat_compute_power` and `gpu_stat_power_per_cycle` from the current language on each tooltip build. Both English and Chinese translations are provided. `AgeOfAgiMod` subscribes on enable without duplicate handlers and unsubscribes on disable.

These initial properties feed the server training and working-night power cycle. Categories are applied separately from CSV by the factory. The helper does not reset values in `onLoaded`. Existing saved items may retain earlier values/tags; test changed definitions by spawning fresh items.

Saved runtime state uses the game's Newtonsoft.Json assembly with `Private=false`; CSV configuration does not use JSON.

## CPU and memory definitions

CPU and memory follow the same Definition → Catalog → Helper structure. `CpuCatalog` reads `Data/Definitions/cpu.csv`; `MemoryCatalog` reads `Data/Definitions/memory.csv`. Each catalog is loaded once per registration batch containing its kind, validates the full definition-to-item mapping before changing factories, and remains captured by those factories. The strict rules above also apply, except CPU multipliers accept fractional numbers.

| Kind | Required CSV columns beyond `id` | Helper queries |
| --- | --- | --- |
| `cpu` | `powerBoostRate`: finite positive float (120 means +120%, or ×2.2 with one CPU); `powerPerCycle`: non-negative integer | `CpuState.IsCpu`, `GetPowerBoostRate`, `GetPowerPerCycle` |
| `memory` | `memory`: positive 32-bit integer, in GB | `MemoryState.IsMemory`, `GetMemory` |

CPU initialization stores `CPU`, `POWER_BOOST_RATE` (float percentage-point bonus), and `POWER_PER_CYCLE`. Memory stores `AGE_OF_AGI_MEMORY` and `AGE_OF_AGI_MEMORY_GB` (integer). Their tooltip hooks resolve `cpu_stat_power_boost_rate`, `cpu_stat_power_per_cycle`, and `memory_stat_capacity` from en/zh translations on each build. Both hooks follow the GPU enable/disable lifecycle and read current item tags without resetting saved state. ServerHardware sums these instance properties for training, memory checks and working-night power.

## Machine definitions

`Definitions/MachineDefinition.cs`, `Catalogs/MachineCatalog.cs`, and `MachineParameters.cs` provide shared metadata. DiskWriterHelper, CompilerHelper, ServerHelper and DataCollectorHelper each own their complete initialization, load repair, interactions and tooltip entry point. `Data/Definitions/machines.csv` contains `age_of_agi.disk_writer` (`powerPerCycle: 3`, common CSV `base_value` 150) and `age_of_agi.compiler` (`powerPerCycle: 10`, shared by every assembly). Their CSV `types` cell is `machine`, restored to native `MACHINE` during initialization so the game supplies the category label and machine tags.

Each CSV row requires a stable `id` and non-negative Int32 `powerPerCycle`. Price belongs exclusively to the common CSV. The strict column, integer, duplicate-ID, and mapping checks apply before any factory replacement. Load this catalog once per registration batch containing machines and capture it in the factories. Every machine CSV row requires a definition, including serverbox. Serverbox starts at zero power and updates its instance cost from installed hardware.

`InitMachine` sets `MACHINE` and `MACHINE_POWER_PER_CYCLE`. `IsMachine` identifies configured machines; `GetPowerPerCycle` reads their current per-operation cost. Each machine helper owns its late tooltip hook and resolves `machine_stat_power_per_cycle` in en/zh on every build and is removed on disable. The draft machine receives its common CSV price and does not receive this operation-cost tooltip.

This is an operation cost for the disk writer and compiler. `DiskWriterHelper` executes immediately through the native button and recipe system described below; it does not register automatic daily `machinery_drawn_power`. The native bottle printer is the UI/execution reference rather than machines that require a module bay or nightly progress. DataCollectorHelper instead uses the value as its native daily battery draw.

## Data collector

`age_of_agi.data_collector` costs 200, uses the native machine category and `metal_machine` audio, and stores existing normal and high-capacity data containers. Its storage indices are power 0, network 1 and containers 2. High-speed paired switches connect its containers to distributed processing terminals. The 4×6 container grid accepts only items recognized by `DataContentState.IsContainer`; it does not introduce another container item type. Applying a stack of at least six `disk_empty` items to the machine consumes six and expands the grid once to 8×6, preserving installed containers and excess disks. `AGE_OF_AGI_DATA_COLLECTOR_EXPANDED` records the current expansion state. The native daily cycle draws 2 energy from the installed battery without overdraw. `data_collector1` appears whenever at least one data container is present; an empty expanded machine uses `data_collector0`.

## Disk writer: native UI and instant recipes

`Logic/DiskWriter/DiskWriterRuntime.cs` builds `GameSlotInventory`, `GameGridInventory`, `MachineHelper.CreateMachineryWindow`, and `MachineryButton`, following `MachineBottlePrinter`. It attaches a native content window and uses `MachineHelper.SetupBatterySlot`, native container/important tags, and small-machine movement audio. It does not claim `STANDARD_MACHINE_TAG`, whose native helpers assume a module bay at storage index 1.

| Storage index | Panel role | Size | Accepted input |
| --- | --- | --- | --- |
| 0 | `power` | 1×2 single slot | Native battery-slot rules |
| 1 | `input` | 2×2 single slot | `age_of_agi.disk_empty` only |
| 2 | `chip` | 1×1 single slot | `age_of_agi.part_enc_chip` or `age_of_agi.part_dec_chip` |
| 3 | `output` | 6×4 grid | External insertion denied; extraction allowed |

The `recipe` panel contains a sprite-only component preview above the Choose component button; after choosing, its caption becomes Change component. `control` contains Execute. Neither panel adds a storage node. The retired template slot was removed outright during development; old five-bay saves have no migration. Native save decoding restores child items by node index, so retain the new power/input/chip/output order after release. Ordinary inputs follow native ownership checks. `onLoaded` repairs structural markers and missing cost metadata without resetting a saved cost, then restores the selected recipe, preview image and disk-state sprite.

`Data/Definitions/disk_writer.csv` is loaded once per registration batch. Its ordered rows contains five stable IDs, `shape_0` through `shape_4`, with explicit `decoderItemId` and `encoderItemId` outputs. Recipe IDs are not item IDs. Validation requires unique IDs and outputs, the correct CSV kinds and complete encoder/decoder coverage. The source chip selects the family; the machine's saved recipe ID selects the shape. No template items, factories, localization entries or template slot remain; the placeholder image has been removed.

`DiskWriterSelector` opens a separate native machinery window beside the button's current position, with five component image buttons in a vertical stack and Cancel at the bottom. Native window positioning clamps it to the screen; it can be dragged. Option names are available on hover. The chosen image is rendered by `DiskWriterComponentPreview`, a 48×48 sprite-only widget preserving the sprite's aspect ratio. It has no inventory, item label, background grid or raycast target. Temporary item instances only resolve each sprite/name and are immediately destroyed; shared mod sprites are reused. Save only the recipe ID in `AGE_OF_AGI_DISK_WRITER_RECIPE`. A fresh machine requires an explicit choice; cancel preserves the old choice. Replacing the chip retains the shape choice but updates the image family. Missing/unknown choices disable execution and clear the image. The chooser closes on selection, cancel, chip replacement, machine closure or hiding, and is destroyed with the machine/UI while removing its graph root. Reopening reuses the same window. Products keep their native sprite-derived shapes. Shared compiler shape comparison lives in `Items/ItemShapeHelper.cs`.

Each click validates input IDs, ownership, the selected recipe, current power cost, battery energy and output placement. A native `CraftingRecipe` declares one blank disk as material, the source chip as a retained tool, and the battery as a powered tool. Reserve output first, then recheck input instances, recipe ID, output family and power cost before native `Craft` consumes one disk and the configured power (normally 3). Refresh battery and machine sprites afterward. Missing inputs, insufficient power, full output, rejected transfer or a mid-transfer selection change preserve resources. A reentrancy guard prevents duplicate execution. Output admission opens only for the internally generated item and closes in `finally`; incomplete products and temporary sprite-lookup items are cleaned up on failure.

For a manual test on game 0.46D, restart and spawn a fresh `disk_writer`, `disk_empty`, `part_enc_chip` (or `part_dec_chip`) and charged vanilla battery. Check that the chooser opens beside the button with five vertical image options. Select one and check the non-interactive image above Change component, without item labels or background grid, then Execute. Confirm one disk and 3 energy are consumed while the chip and choice remain. Test cancel, closing/hiding the machine, swapping chip families, all ten outputs, full output, insufficient energy, state sprites and save/reload with occupied slots and a choice. Third-party automation compatibility depends on native inventory-role discovery. Scene substitutes do not verify actual Unity layout, screen-edge positioning or real save round trips.

References: `gamecode_ref/Assembly-CSharp/MachineBottlePrinter.cs`, `CraftingHelper.cs` (temporarily opened output inventory), `CraftingRecipe.cs` (retained tools, powered tools, material consumption), `MachineHelper.cs`, and `GraphUtils.cs` (`TryAcceptAll` returns the untransferred amount; zero means success).

## Compiler: immediate whole-grid assembly

中文完整说明见 [Compiler（自动编码器）实现说明](../Docs/Items/Compiler.md)，包括使用流程、配对规则、属性公式、配置字段、存档兼容与故障排查。

`CompilerHelper` uses the native machinery window, battery setup, small-machine audio and `CraftingRecipe`, with a Compile button and multiline output preview. Compilation is immediate; no `onCycleEndSlotItemFunc` is registered. `compiler_working` means the current inputs, battery and output space permit compilation. The new control panel contains no inventory and keeps saved storage indices stable:

| Storage index | Panel role | Size | Accepted input |
| --- | --- | --- | --- |
| 0 | `power` | 1×2 single slot | Native battery-slot rules |
| 1 | `base` | 2×2 single slot | Owned base model |
| 2 | `grid` | Resizable; hidden without a base | Owned configured AI parts, one unit per placed component |
| 3 | `output` | 6×4 grid | Only the internally generated product; player extraction allowed |

The base supplies space and a model-memory multiplier from `base_model.csv`: grid 4×6/×1, neural 3×12/×2, tsfm 6×8/×4, jepa 8×12/×8. It never changes task, training work, compute or durability, and compilation retains it without durability loss. A populated grid locks its base with native `LOCKED_TAG`; emptying it unlocks the retained base.

`ai_parts.csv` defines all 16 code parts, including regularization and three task heads using `bio_head`, `malicious_head` and `sensory_head`. Encoder/decoder definitions initialize instance Tags for base compute bonus percentage points, memory GB, training work and independent training data. Shapes 0–2 each contribute +10%, 2 GB, 40 work and 10 data; shapes 3–4 each contribute +15%, 4 GB, 80 work and 20 data in each family. Read actual instance Tags when compiling; loading only fills missing legacy fields. Prices remain in CSV. Components, source chips and models retain their existing IDs.

`compiler.csv` now has exactly one `compile_assembly` recipe, outputting `ai_untrained`. It configures initial durability (10), duplicate durability retention (0.9), link compute multiplier (1.2) and feedback training retention (0.8) and regularization multiplier (1.2). Electricity is exclusively a machine instance parameter, initialized to 10 from `machines.csv`; existing saved costs are preserved and missing cost metadata is backfilled. The old `power`, `requiresLink`, priority and fallback-power checks have been removed.

`CompilerAssemblyCalculator` validates and includes every grid component, requiring at least one encoder and one decoder. It rejects foreign/unconfigured parts, unowned items, invalid or missing basis attributes, stacks, overlaps, out-of-bounds layouts more than one task head or more than one regularization component. Unpaired basis components retain all base contributions; every placed component is consumed, including functional parts and task heads. No head selects `computing`; heads select `sensory_analysis`, `intelligence_intrusion` or `biological_analysis`. There may be at most one head in total, even of the same type, independently of the at-most-one regularizer.

Links use cardinal contact between occupied cells, matching native node adjacency without diagonals or bounding-box shortcuts. Each functional part offers encoder/decoder pairs regardless of numeric shape. A global maximum-weight bipartite matching maximizes paired base compute and can reassign overlapping candidates. Each basis receives link benefit once. Feedback separately matches only the two short ends of its 4×1 straight mask, including rotations/reflections, and reduces each paired basis's work by 20% once. A basis may receive both link and feedback. `AiPartsHelper` keeps the native 4×1 artwork and footprint and restores earlier 3×1 overrides on load while retaining their transform; both normal and drag images are sized consistently.

Predicted compute is the sum of base percentage-point contributions, each multiplied by 1.2 if linked; a displayed +20% corresponds to a deployment multiplier of 1.2. Component memory is summed without functional discounts, then architecture memory modifiers and the base memory multiplier are applied before one final ceiling. Training work is summed after individual feedback discounts and preserves fractional values. Training data is a separate Int32 sum of component data Tags, without feedback discounts. Group duplicates by `(encoder/decoder family, numeric ID suffix)`; each occurrence after the first multiplies predicted initial durability by 0.9. Rotation/reflection does not change duplicate identity. Apply regularization (×1.2 when present), then round final durability with midpoint away from zero. No per-step rounding is applied; durability may exceed 10.

`AiModelState.Apply` freezes task, training work, independent training data, memory, predicted compute bonus and predicted initial durability in the output's own Tags with schema version 2, then assigns the localized task-based name. It copies no ownership, lock, machine, component or price state. Saved models do not recompute when catalogs change. The normal factory leaves model attributes empty; the F9 action explicitly initializes its debug model. Legacy models lacking compiled attributes remain invalid for training. Server training converts frozen predictions to equal actual attributes; see [Server.md](../Docs/Items/Server.md).

Preview and execution share the calculator. `PixelWindow.AddInventoryStateChangeAction` refreshes moves/rotations and other native inventory updates. Execution creates the fully attributed product, reserves output, then checks actual input instances, layout/attributes, machine cost, battery and output identity again before native `Craft` consumes the entire assembly and power. Transfer failures clean the product without paying; internal-output and execution guards block external injection and callback reentry.

For manual verification on game 0.46D, restart, spawn a compiler/base/battery and components, inspect the live preview, then click Compile and inspect all six model properties. Test links, feedback rotation, task heads, conflicting heads, duplicates, full output and low power. Save/reload an occupied machine and a compiled model, checking slot binding, base lock, feedback footprint and unchanged model values. Automated checks use API substitutes and cannot prove Unity rendering or real save-file round trips.

## Hardware movement audio

服务器训练与维护见 [Server.md](../Docs/Items/Server.md)；已实现的BTC加工、情报写入、无损基因选择和开发期参数见 [Production.md](../Docs/Items/Production.md)。只有成品AI成功完成生产批次才扣1点耐久，训练不扣。复杂数据容器使用原版DNA盘字段，初始4次使用；种子不消耗，交换机升级作为条件保留。

Hardware audio is selected per item through `items.csv` and the preset dictionary in `Items/ItemAudioPresets.cs`. The migrated values are `gpu0=module`, other GPUs `small_machine`, and CPU/memory `small_module`. Their owning Helpers invoke the delegate captured during registration; they contain no ID-based audio branches or fallback preset calls. Change `audio_preset` to select another native preset. A future custom preset can assign mod sound IDs from `ModHelper.GetSoundIdentifier(ModConstants.ModId, key)`. The game's drag, placement, and quick-transfer handlers decide when to play those IDs; do not play audio while creating items.

References: `gamecode_ref/Assembly-CSharp/AudioHelper.cs` (`InitModule`, `InitSmallModule`), `ModuleDirectory.cs` (chip/module factories), and `ItemMouseDragHandler.cs` (`StartDrag`, `EndDrag`). Restart and spawn fresh hardware to verify new properties and sounds on a test save.

## Add and test an item

1. Export its PNG into `Textures/Items/`.
2. Add a mapping row and both translations.
   For `gpu`, `cpu`, `memory`, or a new `machine`, also add a matching entry to its definition CSV catalog for type-specific parameters; keep the price in CSV.
3. Build `Aoa.csproj` in Release. The output folder contains `Aoa.dll`, `manifest.xml`, and runtime asset folders.
4. A successful build automatically copies the DLL, manifest, Data, Localization, Textures, Sounds, LootTables, Books, and optional preview into `Mods/AgeofAGI/`. Copies run every build without relying on timestamps; installed game dependencies and `art_src/` are excluded. Exit the game before building and restart it after deployment.
5. On a test save, use F9 > Spawn and search the item ID suffix (for example `gpu0`). F8 reports the actual registered item count and any missing translations.

`Spawn ai_untrained` initializes a trainable debug model. Its task, training work, data budget, memory, predicted compute bonus and durability are the inline `CompilerAssemblyResult` arguments inside `SpawnItem` in [ModDebugActions.cs](../Debugging/ModDebugActions.cs). Edit them, rebuild, restart and spawn a new model. `Spawn ai_trained` similarly creates a computing model with 2GB memory, +20% compute and 10 durability. These defaults apply only to F9 actions.

The root `art_src/` folder stays in the development project. The mapping is read with `ModHelper.ReadModFileText`, so it requires no `files` capability.

Saving an image alone does not deploy it; run `dotnet build` after edits. Use `-p:DeployModOnBuild=false` when you only want build output. Deployment overwrites current assets but does not delete files removed from the source directory; clean up retired runtime files deliberately.

## Validate mappings

From the game root, with the .NET 10 SDK installed:

```powershell
dotnet run --project .\Modding\Tests\ItemCatalogChecks\ItemCatalogChecks.csproj -c Release
```

This checks initial/state PNG coverage and merged item identities, localization, CSV validation, registration cleanup, common CSV prices with Int64 limits and type-helper isolation, property assignment, categories, optional text, and all 10 disk-writer recipes. Tests link the actual ILSpy-exported `CraftingRecipe` and filter sources from `gamecode_ref`, read real PNG alpha masks, and substitute scene/UI/inventory APIs. Disk-writer checks cover native UI structure, vertical image selection/cancel, adjacent-window positioning and lifetime, non-interactive selected sprites without labels or grids, choose/change captions, chip-family switching, saved recipe restoration, slot whitelists, retained source chips, cost/consumption, native output shapes, full output, transfer failures, reentrancy, and four reconstructed slot bindings. Compiler checks additionally cover base-model grid sizes, the hidden/shown grid panel, native base locking, idle/working sprite switching, immediate whole-grid compilation, maximum-benefit link matching, feedback endpoints/rotation, duplicate durability, task-head conflicts, live preview, persisted model attributes, failure conservation and rebuilt slot bindings. Server checks additionally cover independent bay upgrades, working-night training and power, locks/pauses, final output transactions, persisted tasks and compatibility with retired display fields. Production checks cover BTC, intrusion upgrades, nondestructive gene selection, native DNA fields and four uses, per-batch durability, last-use delivery, pauses, output failures and saved task identity. Actual Unity rendering, native inventory packing, sounds, DNA equipment and save-file round trips still require a test save.

## GPU magnifier inspection

All configured GPUs (gpu0 through gpu3) accept the native magnifier via INSPECTABLE_TAG. GpuHelper also restores this structural tag after loading older GPUs, without reapplying hardware attributes. GpuInspectionHelper adds the shared gpu_detailst.png image to the native fuel small/large view hosts. Native lens movement, masking and reset positions remain in use; original graphics are temporarily hidden and restored by ResetUI before opening another item. Repeated inspection reuses the image nodes; disabling the mod closes GPU inspection and destroys those nodes. No appraisal registry, ratings, defects, inspection text or price effects are added. Restart after building and use a magnifier on any GPU to check the illustration and lens alignment on game 0.46D; automated UI substitutes verify switching, cleanup and saved attributes but do not verify actual Unity rendering.

## Independent training data

New compiled models separately freeze work W and data budget D. S01 charges cumulative ceil(D * progress / W), minus data already paid; completion pays exactly D. Feedback reduces W only. A missing positive independent data budget is invalid. There is no fixed work-per-data setting, unpaid-progress offset, or historical task migration. See [Training.md](../Docs/Items/Training.md) for formulas, configuration, current-format state and examples.
