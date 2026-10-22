# Managed K variable shortcut labels

## Problem and behavior

V3.2.55 stopped before collecting seven completed steel works at 2026-10-22
23:47 and 2026-10-23 06:08. Both captures were ordinary fields with independent
minimap/skill evidence. On the second capture, the mail `Y` key shifted `Home`
left. Whole-icon matching and the original fixed Home location were insufficient.

The managed screen now keeps the existing 3/4, K/I and Home/End routes and adds
three tight shortcut glyph matches: Home, End and ESC. It recognizes the two
observed key-row layouts, anchored by End/ESC. Four small generic UI references
include two observed Home renderings. No detection threshold was lowered.
Exact compact OCR is a fallback only when existing routes and glyphs are weak.

Every managed K, including the first, needs the same route on two actual fresh
frames within the existing two-second decision window. Glyph observations must
also retain their method and geometry, with independent field structure on both
frames. A fresh CLI idle check precedes a final screen verification on the same
capture used to authorize the real input. The selected route must still hold;
one template frame and one OCR frame are not combined into permission.

Five older incidents had the same error text while the actual screen was the
food quick-crafting list. Its cyan first navigation tab defeated the neutral-only
generic navigation detector. It is now classified before the unknown/first-K
branch using bright list/navigation geometry plus exact food heading and
processing-tab OCR. Two captures, CLI idle and a final verified capture precede
one dynamic processing-tab click. This path sends neither Esc nor K and does not
repeat an unconfirmed food-tab click within an entry attempt.

## Validation scope

The offline corpus contains 209 readable, canonical 800x1000 incident frames:
208 previous frames and the new 06:08 frame. The reference and expected layout
were chosen with these incidents visible: this is regression validation, not an
unseen accuracy benchmark.

- Same-error normal fields: 5/5 have a baseline or new visual route.
- Same-error food lists: 5/5 remain ineligible as fields and meet the dedicated
  food-navigation visual classifier.
- Other menus/modals/results/loading screens: 0/140 acquire the new world-key
  evidence or the combined static-image route.
- All world-HUD images, including busy/combat contexts: 66/69 have static visual
  evidence. This category is not proof of CLI idle or permission to input.

These figures were first computed offline with the same OpenCV binary-glyph
matching operations. The Windows `Managed K variable HUD validation` job runs
the actual compiled production matcher against 209 sanitized shortcut strips,
the five food-menu heading/navigation strips, and separate temporal-policy
regressions. Its status must be checked on the final PR commit.

Only generic shortcut/food-menu UI pixels are committed. Complete private game
screens, player names, chat, maps, inventories and recipes are excluded. Original
frame SHA-256 values identify the offline inputs. Baseline and independent-field
booleans in the manifest are offline observations, not CLI evidence. A single
stored picture is never copied and counted as two actual game frames.

## Limits

No live game input, menu transition, receipt, PC installation or rollback was
performed. Exact Korean OCR and real menu navigation still require game-PC
validation. This change does not alter the updater version or release anything.
