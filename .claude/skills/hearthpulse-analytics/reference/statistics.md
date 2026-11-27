# Reading and comparing Hearthpulse numbers

> Adapted from [anthropics/knowledge-work-plugins](https://github.com/anthropics/knowledge-work-plugins)
> `data/skills/statistical-analysis/SKILL.md` at `e91a134` (sections "Sample Size Considerations", "Practical
> Significance vs. Statistical Significance", "When to Be Cautious About Statistical Claims"), Apache-2.0, see
> [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md). Rewritten for game statistics; examples are this project's.

Read this before ranking rows, comparing two groups, or writing a sentence about what a number means (UI text,
release notes, a report). The thresholds themselves are in the catalogue and are not restated here.

## Which summary fits

| Quantity | Summary | Why |
|---|---|---|
| Win rate, pick rate, top-4 share, keep rate | share with a Wilson interval | bounded 0..1, small samples common |
| Battlegrounds place (1..8, Duos 1..4) | mean with interval **and** the distribution | the mean hides a hero that finishes first or eighth; `dist` shows it |
| Game length, turns, damage | median and quartiles; mean only beside them | right-skewed: a few long games pull the mean |
| Counts per card or hero | the count and its denominator together | a rate without its `den` cannot be judged |

A value never appears without its sample size. Wide intervals signal uncertainty; do not infer the significance
of a difference from either row's interval alone.

## Is the difference real, and does it matter

- Assess a difference with an interval or test for that difference, accounting for dependence between observations
  from the same installation. Overlapping marginal intervals can still accompany a significant difference;
  they prove neither equality nor absence of a difference.
- A real difference can be too small to act on. State its size and uncertainty and explain its practical relevance
  to the decision; do not declare a fixed win-rate difference universally irrelevant.
- With a small sample, say so instead of rounding it away: "12 games, top-4 between 32% and 81%".
- Precision follows the sample: 3 decimals are the wire format, not the display. Show whole percent under a few
  hundred games, one decimal above.

## Biases this data has

| Bias | Here | What to do |
|---|---|---|
| **Who shares** | pool contributors are players who installed a tracker and kept sharing on; they place above the ladder's average (`server/pool/README.md` says so for the MMR percentiles) | never call pool numbers "the ladder"; external and own numbers are different populations: a new metric does not average them (the one existing sample-weighted mix is the advisor's meta, `PoolClient.Merge.cs`) |
| **Heavy players** | one installation may contribute many captures to a group; `bg-hero-1` has no per-installation cap | read `sources` beside `captures`; a row carried by few installations is weak even above the threshold |
| **Reinstalls** | an installation is the hash of its install id; a reinstall is a new source | the 5-installation floor is a privacy floor, not a count of people |
| **Survivorship** | abandoned games, games without a result and imported games are left out | a metric about "games" is about finished, locally recorded games; early concedes that never reach a result are missing |
| **Selection in offers** | heroes rerolled away before the pick are not in `offered` | pick rate is "picked when still offered at the pick", not "picked when first seen" |
| **Skill and choice** | strong players pick strong heroes, decks and lines | "players who pick X place better" is not "picking X makes you place better"; write the first |
| **Region and time** | the external collectors read one region; slices are up to days old | show the date; do not present a slice as global or current |
| **Patch mix** | a balance patch changes the population mid-window | captures: group by `start.build`; record views: by balance patch, with the 7- and 30-day windows inside it (ADR 0014); never merge two balance patches to reach a threshold |

## Simpson's paradox: check before merging groups

A hero can look better overall and worse in every MMR bracket when its players sit in higher brackets. Before
publishing a number that merges builds, ranks, MMR brackets, formats or modes, check that the direction holds inside
the groups; when it does not, publish the groups and drop the merged number.

## Many rows, one page

A tier list ranks many heroes or archetypes at once. Individual 95% intervals do not provide 95% simultaneous
coverage of the whole list. Sorting noisy estimates also tends to exaggerate the extremes; assess uncertainty
and multiple comparisons before treating the first row as a reliable winner.

- Sort by a value only among rows that clear the sample floor; keep `small_sample` rows out of the ranking, not at
  its top.
- For ordering, prefer a shrunk estimate or the interval's conservative bound over the raw value, and say which.
- Do not report "the best hero for tribe X" found by scanning every hero and tribe pair without saying how many
  pairs were scanned.

## Wording

| Supported | Not supported |
|---|---|
| "Picked in 30% of the games where it was offered (40 games, 14 installations)" | "Players prefer it" |
| "Mean place 3.9, between 3.1 and 4.8" | "A 3.92 hero" |
| "Not enough games to show a value" | a dash, a zero, or last patch's number |
| "Hearthpulse data, updated 12 Nov" for external numbers | "Hearthpulse statistics" for numbers we did not collect |

User-facing strings go through Loc keys in both languages: `better-writing`.
