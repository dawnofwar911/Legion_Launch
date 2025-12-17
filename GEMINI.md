GEMINI.md - Project Context: "LegionDeck" (Working Title)
1. Project Overview
Goal: Build a custom, unified game launcher and wishlist aggregator specifically optimized for the Lenovo Legion Go 2 (Windows 11 Handheld). Core Value Proposition:

Wishlist Consolidation: Primary focus on Steam Wishlist aggregation.

Subscription Intelligence: Cross-references wishlists against active subscriptions (Xbox Game Pass, EA Play Pro, Ubisoft+) to flag "Free to Play" titles.

Handheld Optimization: Designed for controller navigation (XInput) and Legion Go hardware quirks.

2. Technical Architecture
Core Stack
Language: C# (.NET 8.0)

Interface: CLI (Phase 1) -> WinUI 3 / WPF (Phase 2)

Authentication: WebView2 (Edge Chromium) for cookie extraction (Reverse-engineered from Playnite).

Database: SQLite (local cache of games/wishlists).

Module Breakdown
Auth.Manager: Handles login flows.

Mechanism: Spawns hidden WebView2 instances to hit official login pages, waits for specific session cookies (e.g., steamLoginSecure, x-xbl-contract-version), extracts them, and stores them in encrypted local storage.

Data.Aggregator:

Steam: Public API for wishlists (or authenticated scraping for private).

Xbox/Epic/GOG: Used primarily for subscription status checks (e.g. Game Pass availability), not full wishlist syncing.

The.Oracle (Logic Engine):

Takes the aggregated wishlist.

Queries IsThereAnyDeal (ITAD) API (or IGDB) to check current subscription status.

Outputs a GameObject with flags: IsOnGamePass, IsOnEAPlay, IsOnUbiPlus.

System.Launcher:

Scans local registry/manifests to find installed games.

Executes games via their URIs (e.g., steam://run/ID, xbox://launch/ID).

3. CLI Interface Specification (Phase 1)
The CLI is the Proof of Concept (POC) and logic verification layer.

Command Structure: legion [verb] [options]
Command,Sub-command,Arguments,Description
legion,auth,--service [steam/xbox/all],Opens WebView2 window to capture session cookies.
legion,sync,--wishlist,Pulls latest wishlist data from all auth'd sources.
legion,sync,--library,Scans local drive for installed games.
legion,check,--subs,Cross-refs wishlist against Sub APIs. Returns JSON/Table of matches.
legion,launch,--id [game_id],Launches a specific game.
legion,config,--set-api-key [key],"Sets API keys (ITAD, IGDB)."

4. Development Roadmap
Phase 1: The Brain (CLI)
[ ] Step 1: Scaffolding. Set up .NET 8 Console App with Dependency Injection.

[ ] Step 2: Authentication. Implement Playnite.Common inspired WebView2 login for Steam & Xbox.

[ ] Step 3: Data Ingestion. Fetch Steam Wishlist JSON and parse it.

[ ] Step 4: The Oracle. Implement ITAD API client to check "Starfield" -> "GamePass".

[ ] Step 5: Output. CLI prints a table: "Game | Price | Subscription Status".

Phase 2: The Face (GUI)
[ ] Step 1: Port logic to WinUI 3.

[ ] Step 2: Design "Grid View" with Controller Navigation (XInput).

[ ] Step 3: Implement "Badges" (Green for Game Pass, Red for EA).

5. Key Resources & References
Playnite Extensions Source: github.com/JosefNemec/PlayniteExtensions (Reference for Auth/Cookie logic).

Steam Web API: api.steampowered.com

IsThereAnyDeal API: api.isthereanydeal.com

Windows URI Schemes:

Steam: steam://

Xbox: ms-xbl-number:// or xbox://

Epic: com.epicgames.launcher://

6. Current Context / Active Task
Status: Phase 2 Development.
Next Action: Implement Controller Navigation (XInput) and Visual Polish for the Handheld UI.

Roadmap Progress:
[x] Phase 1: The Brain (CLI) - COMPLETE
[x] Step 1: Scaffolding (Shared Core Library)
[x] Step 2: Authentication (Moved to Core)
[x] Step 3: Data Ingestion (Moved to Core)
[x] Step 4: The Oracle (Moved to Core)
[x] Step 5: Output (CLI functional and verified)

[ ] Phase 2: The Face (GUI) - IN PROGRESS
[x] Step 1: Port logic to WinUI 3 (Self-contained, Shared Core).
[ ] Step 2: Design "Grid View" with Controller Navigation (XInput).
[ ] Step 3: Implement "Badges" (Green for Game Pass, Red for EA).