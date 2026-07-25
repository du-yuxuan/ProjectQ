# Repository Guidelines

This repository holds analysis and documentation for the **AdventureX 2025** hackathon (222 projects, 43 tracks, 119 award winners). It is documentation-only — there is no source code, build system, or test suite.

## Project Structure & File Organization

All files sit at the repository root as paired Markdown (`.md`, for reading) and Excel (`.xlsx`, for filtering) datasets:

- `AdventureX_2025_Tracks.{md,xlsx}` — the 43 tracks (5 themed + 38 sponsored), prizes, participation.
- `AdventureX_2025_Gallery.{md,xlsx}` — all 222 projects grouped by themed track, with stats, Top-10 lists, and per-project detail.
- `AdventureX_2025_Analysis_Report.md` — deep report on judging logic and award patterns (no Excel pair).

## Document Conventions

- **Language**: Simplified Chinese for all prose, headings, and table cells; reserve English for product/tech names (e.g., "Next.js", "Multi-Agent").
- **File naming**: `AdventureX_2025_<Scope>.{md,xlsx}` (e.g., `AdventureX_2025_Gallery.md`).
- **Headings**: Gallery/Tracks use emoji + Chinese label (`## 📊 项目统计`, `## 🏷️ All We Do 何以为人`); the Analysis Report uses numbered Chinese headings (`## 一、竞赛全景概览`).
- **Front matter**: every doc opens with a `>` blockquote giving the data-source URL and `提取日期：YYYY-MM-DD`.
- **Tables**: Markdown pipe tables; currency as `RMB`; numbers with thousands separators; separate major sections with `---`.

## Keeping Markdown & Excel in Sync

`.md`/`.xlsx` pairs must stay identical. Edit one, regenerate the other from the same dataset, and commit both together. The Markdown is for reading; the Excel is for sorting and filtering.

## Updating Data

Source of truth is the AdventureX Gallery site. To refresh: pull from `gallery.adventure-x.org`, update both file formats, bump each `提取日期`, and recompute aggregate counts in `AdventureX_2025_Analysis_Report.md`.

## Commit & Pull Request Guidelines

No Git history exists yet. Once initialized, use summary lines under ~72 chars, e.g. `更新 Gallery 至 2026-07-20 抓取`. Bundle each `.md`/`.xlsx` pair in a single commit and reference the extraction date. Pull requests should state which datasets changed and include the new `提取日期`; no screenshots are required.
