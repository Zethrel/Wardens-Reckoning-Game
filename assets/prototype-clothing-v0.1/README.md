# Clothing layers, trial (not adopted)

From Clothing Overlay Trial 01, interchange revision 2
(`wardens_clothing_overlay_interchange_rev2.zip`, sha256 c1994b0c…3db8, in the
design folder's `_Temporary_Files_need_review\clothes\interchange_trial_rev2`,
16 Nov 2026), its `godot_fixture` pictures, copied as they came. Reviewed in
that folder's `CLAUDE_REVIEW.md`. Shown in the asset preview's clothing patch (C).

The adopted Human v5, Dwarf v5 and Halfling v7 taken apart into layers, each a
32 × 48 picture with the same canvas and feet:

| Layer | What it is |
|---|---|
| `neutral` | the clothing-free body, in underclothes (mauve shirt, dark-blue shorts) |
| `runtime_reference_lower` | the sprite's own trousers, completed up to the waist under the old tunic hem |
| `reference_upper` | the sprite's own tunic (with belt) |
| `alternate_upper` | a green short-sleeved tunic with a wrap collar |
| `alternate_lower` | blue-grey trousers |
| `boots` | the sprite's own boots |
| `head` | the head, hair and beard, drawn last |

Drawn in the order neutral, trousers, tunic, boots, head, the own outfit is
pixel for pixel the adopted sprite.

**Status:** a test for the maintainer to judge. The neutral bodies, the
layers, the completed trousers and the green, blue-grey and underclothes
colours are not adopted.

| File | sha256 |
|---|---|
| `dwarf_v5/alternate_lower.png` | c40eac74a1415a2e… |
| `dwarf_v5/alternate_upper.png` | 34ea9339842497d5… |
| `dwarf_v5/boots.png` | 492db865c592846d… |
| `dwarf_v5/head.png` | de6b2811cacfb2a0… |
| `dwarf_v5/neutral.png` | 5b6bba48cea5633b… |
| `dwarf_v5/reference_upper.png` | 75f9bf1a651f5d30… |
| `dwarf_v5/runtime_reference_lower.png` | e51e0ac22602f154… |
| `halfling_v7/alternate_lower.png` | ef98aeb71ddb4f9a… |
| `halfling_v7/alternate_upper.png` | 33dbc1e94f6c0080… |
| `halfling_v7/boots.png` | 17bf13c47980919c… |
| `halfling_v7/head.png` | 0c35aed8bd0588ff… |
| `halfling_v7/neutral.png` | 1ab08929777aaf43… |
| `halfling_v7/reference_upper.png` | a11a67648ba9db33… |
| `halfling_v7/runtime_reference_lower.png` | 23ea2a5cb3f76bc8… |
| `human_v5/alternate_lower.png` | 90cb0b58cf558f77… |
| `human_v5/alternate_upper.png` | f2250fc8e38fc1b5… |
| `human_v5/boots.png` | d829b1a2ed93de33… |
| `human_v5/head.png` | 52af0ce06bcd9b16… |
| `human_v5/neutral.png` | 5a28e507ea50d468… |
| `human_v5/reference_upper.png` | 49edf9013e45d2ce… |
| `human_v5/runtime_reference_lower.png` | 44f680070696e23b… |
