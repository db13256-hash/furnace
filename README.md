# FurnaceSpeed

An [Oxide / uMod](https://umod.org/) plugin for the game **Rust** that increases the
smelting and cooking speed of every furnace-type object on your server:

| Container | Default |
|-----------|---------|
| Small Furnace | ✅ 3× |
| Large Furnace | ✅ 3× |
| Oil Refinery | ✅ 3× |
| Campfire | ✅ 3× |
| BBQ | ✅ 3× |
| Electric Furnace | ✅ 3× |
| Mixing Table | ✅ 3× |

Each type has its own independent speed multiplier and can be individually enabled or
disabled through the auto-generated config file.

---

## Installation

1. Download **`FurnaceSpeed.cs`** from this repository.
2. Copy it into your server's `oxide/plugins/` directory.
3. Oxide will compile and load the plugin automatically. The config file is written to
   `oxide/config/FurnaceSpeed.json` on first run.

---

## Configuration (`oxide/config/FurnaceSpeed.json`)

```json
{
  "Small furnace": {
    "Enabled": true,
    "Speed multiplier": 3.0
  },
  "Large furnace": {
    "Enabled": true,
    "Speed multiplier": 3.0
  },
  "Oil refinery": {
    "Enabled": true,
    "Speed multiplier": 3.0
  },
  "Campfire": {
    "Enabled": true,
    "Speed multiplier": 3.0
  },
  "BBQ": {
    "Enabled": true,
    "Speed multiplier": 3.0
  },
  "Electric furnace": {
    "Enabled": true,
    "Speed multiplier": 3.0
  },
  "Mixing table": {
    "Enabled": true,
    "Speed multiplier": 3.0
  }
}
```

A multiplier of `3.0` means the container processes items **three times faster** than
vanilla. Set `"Enabled": false` for any type you want to leave at default speed.

---

## Permissions

| Permission | Description |
|------------|-------------|
| `furnacespeed.admin` | Allows use of the `/furnacespeed` chat command |

Grant with:

```
oxide.grant user <SteamID> furnacespeed.admin
oxide.grant group admin furnacespeed.admin
```

---

## Chat Commands

| Command | Description |
|---------|-------------|
| `/furnacespeed <type> <multiplier>` | Update a speed multiplier at runtime |

**Types:** `SmallFurnace`, `LargeFurnace`, `OilRefinery`, `Campfire`, `BBQ`,
`ElectricFurnace`, `MixingTable`

**Example:**

```
/furnacespeed OilRefinery 5
/furnacespeed SmallFurnace 2.5
```

Changes take effect immediately on all running containers of that type and are saved to
the config file.

---

## Console Commands

Reload the plugin at any time without restarting the server:

```
oxide.reload FurnaceSpeed
```

---

## How It Works

Rust's `BaseOven` class runs a repeating `Cook` method every **0.5 seconds** (the default
cook interval). FurnaceSpeed cancels that invocation and restarts it at
`0.5 / multiplier` seconds, so a `3×` multiplier fires the cook tick every ~0.167 s —
three times as often as vanilla, with no changes to item definitions or game files.

On plugin **unload** (e.g. `oxide.unload FurnaceSpeed`), all running ovens are
automatically restored to their default interval.

---

## Compatibility

- **Oxide / uMod** (latest) for Rust
- Tested against Rust Staging and Release branches
