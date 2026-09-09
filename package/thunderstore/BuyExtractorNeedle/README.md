# Buy Dvergr Extractor Needle

Buy configured Dvergr crates without starting a fight. The default extractor crate costs `Coins:4000`, and both the price and supported crate prefabs can be changed in the config.

The nearest Dvergr within 30 meters accepts the payment and answers the player. Large payments are shared with nearby Dvergrs: each helper can carry one full stack, while the main seller keeps everything that remains. To recover the full payment later, every participating Dvergr must be defeated.

If no Dvergr is close enough to accept the payment, the crate is removed for free and does not raise the settlement alarm.

## Configuration

`Required items` accepts comma-separated item prefab names and amounts:

```text
Coins:4000
Coins:2000,BlackCore:1
```

`Target prefabs` accepts comma-separated prefab names. The default value is:

```text
dvergrprops_crate_long
```

The recommended configuration UI is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/). [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/) is also supported.

## Requirements

- BepInExPack for Valheim
- Conditional Config Sync 1.0.5 or later

## Manual installation

1. Install BepInExPack for Valheim.
2. Install Conditional Config Sync.
3. Copy `BuyExtractorNeedle.dll` to `BepInEx/plugins`.

The mod must be installed on the server and every connecting client.

## Dependencies

- [BepInExPack Valheim 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [ConditionalConfigSync 1.0.5](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync/)

Install ConditionalConfigSync as a separate dependency; do not copy its DLLs into this mod's package.

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)

## Discord
[Join server](https://discord.gg/e3UtQB8GFK)
