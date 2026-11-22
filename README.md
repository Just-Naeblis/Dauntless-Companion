# Dauntless Companion
Version 2.3.0

Dauntless Companion adds three things to Dauntless, all set up from one settings window (F10 in game):
  - HEALTH BARS: a health bar for every behemoth you are fighting, in a see-through window on top of the game.
  - HUNT STATS: lifetime stats for each weapon type, such as damage, kills, staggers and parts broken.
  - BUILD SEARCH: finds armor sets for the perks you want, marks the gear you own, and lets you save, share
    and pin builds.

  The Dauntless Forever 1.14.7 launcher has its own built-in boss health bar. If you
  see two bars, turn the launcher's one off, or the one from this mod depending on which one you want to use.

HEALTH BARS
  A behemoth only gets a bar after YOU hit it.
  - Each bar has the behemoth's name above it and its health (and %) inside it. Number Format can shorten
    big numbers (109.2k instead of 109,232).
  - Up to 2 bars show at once by default (Max Bar Count, 1 to 4). Extra bars stack under the first one, or above
    it when the bars sit in the lower half of the screen, so they never grow down into the game's own UI. With
    Horizontal Bar Stacking on they sit next to each other instead. If one more behemoth is hit than there is
    room for, the bar that changed longest ago is replaced.
  - Clones (like Shrowd's) always come after the real behemoth, and a clone only ever replaces another clone's
    bar, so the real behemoth keeps its bar through the clone phase.
  - Damage trail: when a big hit lands (7.5 % of max health or more), the chunk it took off shows as a pale
    highlight that shrinks away. This works on your opening hit too.
  - Shield: a light blue tint with bright edges over the health bar while the behemoth has one. The edges shrink
    as the shield ticks down.
  - Stagger: a thin cyan strip under the health bar fills as you build stagger and slowly drains when you stop.
    It empties when the behemoth is stunned and stays empty while its stagger armor is up (about 30 s). Behemoths
    that cannot be stunned get no strip.
  - Enrage and aethercharge: a red or blue line sweeps across the top of the bar while the behemoth is enraged
    or aethercharged, and sweeps back out when it ends. Set State Indicator Style to Glow for a soft glow
    instead.
  - Death: the empty bar stays for a moment, then shrinks in from both ends while the name fades out.
  - Quarter marks: faint lines at 25 / 50 / 75 % (on by default).
  - Animations play at 60 fps by default, or 30 fps with Animation Refresh Rate set to 30.
  - Colors, slant, gaps and every animation can be changed on the Health Bars pages of the settings window.
  Bars go away on their own 30 s after you last damaged that behemoth, shortly after it dies, and when you go
  back to Ramsgate or the menu.

KEYS
  F10         open the settings window (press it again to close it)
  F8          show / hide the bars
  F4          clear every bar and the pinned build (a bar comes back as soon as you hit that behemoth again)
  F9          preview mode on / off. Sample bars show while it's on, even with no behemoth around.
  While preview mode is on:
    Ctrl+Alt+Arrows     move the bars (hold to go faster). Ctrl+Alt+Home puts them back.
    Ctrl+Shift+Arrows   resize the bars: Up/Down taller/shorter, Right/Left wider/narrower.
                        Ctrl+Shift+Home puts the size back.
    NumPad1             enrage glow on / off
    NumPad2             aethercharge glow on / off
    NumPad3             take a big hit (shows the damage trail)
    NumPad4             kill / revive the bars (shows the death animation)
    The number pad keys need Num Lock on.
  Press F9 again when you're done. Position and size are remembered between sessions.
  Every key can be changed on Settings > Keybinds (click a key, then press the new one, with Ctrl, Alt or Shift
  if you want). The game still sees these keys too. If your graphics driver uses Ctrl+Alt+Arrows to rotate the
  screen, turn that shortcut off in the driver settings.

  Tray icon:  double-click it (bottom right of the taskbar) for the settings window, or right-click it for
              Settings, Show/hide and Exit.
  Alt-tab:    the bars hide whenever Dauntless isn't the window in front (Always on Top on Settings > Overlay
              changes that).

SETTINGS WINDOW
  Press F10 in game (or double-click the tray icon). Press F10 again, click Close, or click anywhere outside the
  window to close it (turn off Close on Outside Click on Settings > Overlay to keep it open). Drag its title bar
  to move it and its edges to resize it; its position and size are remembered.
  - The left column has three groups. Click a group's name to fold it away.
      Health Bars    Health Bar, Stagger Bar, Enrage & Aethercharge, Animations and Layout. Test bars show
                     while one of these pages is open, so you can see each change straight away.
      Hunt Stats     Lifetime Stats (see HUNT STATS).
      Build Search   Find Builds and Saved Builds (see BUILD SEARCH).
  - Settings (bottom left) has tabs for Health Bars, Hunt Stats, Build Search, Overlay and Keybinds. README
    opens this file.
  - Changes save by themselves. Each setting shows what it does and its default. Settings marked "Restart"
    apply the next time the game starts. Reset Tab puts the open Settings tab back to its defaults.
  - Overlay Theme (Settings > Overlay) colors the window after a keystone behemoth: Torgadoro (the default),
    Malkarion, Urska, Thrax, Agarus or Chronovore. It sets the Primary, Secondary and Tertiary colors and the
    window's icon. Changing one of those colors yourself switches the theme to Custom and keeps the icon.
  - The Layout page has a Reset button for the bars' position and size. Settings > Overlay has one for the
    window's own position and size.

SETTINGS.INI
  Every setting is also in settings.ini in the data folder, in the same sections as the window. Each line
  shows the allowed values, the default and what it does.
  - LIVE settings apply as soon as you save the file, even mid-fight. RESTART settings apply the next time the
    game starts.
  - A value that can't be read keeps its default, and the reason is written to the log.
  - If you delete settings.ini, the mod makes a fresh one with the defaults the next time the game starts.
  - A settings.ini from an older version still works as it is: settings are found by name, whatever section
    they're in. Delete it to get a fresh one in the new layout.
  - Colors can be written as #rrggbb or r, g, b.

HUNT STATS
  Hunt Stats is on by default. From your first hunt on, the mod keeps lifetime stats for each weapon type. Open
  Lifetime Stats in the window's left column to see them: the Lifetime tab adds up every weapon, and there is a
  tab for each weapon type at the top. Turn it off on Settings > Hunt Stats (or set hunt_stats = false).
  - Damage: Total Damage Dealt, Biggest Hit, Part Damage, Core Damage, Stagger Damage and Wound Damage. Core
    Damage is what came off behemoths' health, Part Damage what went into their parts, and Stagger and Wound
    Damage what went into the stagger and wound meters. Total Damage Dealt adds up all four.
  - Combat Effects: Parts Broken, Interrupts, Staggers (interrupts not included), Wounds and Status Effects
    Inflicted (hover it to see the count for each element).
  - Hunt Performance: Behemoths Slain, Most Slain Behemoth, Most Used Weapon (Lifetime tab only), Fauna Slain,
    Gatherables Collected, Patrol Chests Opened, Perfect Dodges, Healing Received, Damage Taken, Deaths and
    Bounties Completed.
  - Click Behemoths Slain to see how many times you've killed each behemoth, by its journal name. Journal
    variants like Deepfrost Skarn have their own line; Heroic, Lesser and escalation versions count as the base
    behemoth. On a weapon tab the list shows the kills made with that weapon type (counted from 2.3.0 on).
  - Hunt Stats Layout (Settings > Hunt Stats) switches the tiles to a compact list.
  - The numbers come from the game's own combat counters, the ones its weapon trackers were built on.
  - Each hit, kill or stagger counts for the weapon you're holding at that moment, so swapping weapons
    mid-hunt splits the stats correctly.
  - Hunts run on a hidden server copy of the game on your PC, and that's where the game counts these, so the
    mod counts them there. The copy you play in saves your player name (data\hunt-stats-player.txt) so that only
    your own stats are counted when friends are in your hunt. If you join a hunt hosted on someone else's PC,
    that hunt isn't counted.
  - Stats are saved to data\hunt-stats.ini every few seconds during a hunt and at the end of it. While Lifetime
    Stats is open, the Reset Stats button at the bottom clears them (it asks "Are you sure?" first); deleting
    data\hunt-stats.ini with the game closed does the same.

BUILD SEARCH
  Build Search finds armor sets that reach the perks you want. It is on by default; turn it off on
  Settings > Build Search. Open Find Builds in the window's left column.
  - Equipment: pick a Weapon Type, then a Weapon, or None (any weapon) to search every weapon of that type.
    A legendary weapon gets a Bond Weapon slot, and its bond's perk counts too. The Lantern's cell slot counts
    as well.
  - Modifications: an Omnicell and your weapon's parts. They are saved and shown with the build.
  - Armor: leave a piece on "Any", or pick one to lock it into every build.
  - Perks: one column for each cell type. Left-click a perk to add the most that still fits (+6, otherwise +3),
    click it again to lower it, right-click to remove it. The faint number on a perk is how much more of it
    still fits; perks that can't fit any more are greyed out. Hover a perk to see what it does.
  - Builds: the best ones come first (the most cell slots left free). Builds Shown sets how many are listed, and
    Show More adds that many again. Each card shows the weapon, the armor, the lantern, every cell slot with the
    cell that goes in it, and the perk totals.
  - On a card, the star saves the build, the pin shows it on screen in game, and share copies a build link or a
    screenshot of the card.
  - Clear All (at the bottom) empties the page.
  - Every perk on weapons and armor counts as +3 (power surged), and every cell as +3.
  Your gear: Build Search reads the launcher's save file (state.json; it never changes it). Gear you own gets a
  green outline, gear you don't a red one. With Owned Gear Only on, it only finds builds you can make right
  now. The save is read the first time you open Find Builds; press Refresh Inventory after you get new
  gear.
  Saved Builds: every build you saved. Rename it, write notes, duplicate it, delete it, fold it up (click its
  name), pin it or share it. Import Build adds a build from a build link on your clipboard (from the
  launcher's Builds page or another player). Copy Build Link makes a link the launcher can import.
  Pinned build: a small card on screen in game with the weapon, armor, lantern, omnicell, parts and cells.
  Pinned Build Position and Pinned Build Opacity (Settings > Build Search) change where it sits and how solid it
  is. The Clear Bars key (F4) removes it.
  Other Build Search settings: Picker Style switches the weapon, lantern, omnicell and part pickers between
  picture tiles and a plain list, and Perk Ranks Shown sets whether perk descriptions list +3 and +6 only or
  every rank from +1 to +6.

NAMES
  The bar shows the name written in behemoth-names.txt (in the data folder) for that behemoth, including heroic
  escalation, keystone and trials versions. One that isn't listed shows the game's code name made readable (for
  example "Lerawr"). To add or change a name, add one line:
      lerawr_beta = Lesser Embermane
  The overlay reloads the file by itself. You can find a behemoth's id in UE4SS.log on a line like
      [DauntlessCompanion] Now tracking lerawr_beta (...)
  A bar is matched by the whole id, or by any single word inside it that is a code name, so "sq_rock_normal"
  matches "rock" (Skarn). An id ending in _beta is a lesser behemoth, so it gets "Lesser" in front.

HOW IT WORKS
  A Lua mod can't draw on screen in this setup, so there are two parts:
    Scripts\main.lua         the UE4SS mod. Reads behemoth health from the game and writes it to a small file
                             (data\healthbar.json), and counts your Hunt Stats. It only reads values; it never
                             changes anything in the game.
    src\Core.cs              the overlay: a click-through window that draws the bars, and the settings window.
                             It reads healthbar.json, settings.ini, behemoth-names.txt, build-data.txt and (with
                             Hunt Stats on) hunt-stats.ini. Build Search reads the launcher's save file
                             (state.json) to mark the gear you own, and never changes it. It only writes files in
                             the data folder (see FILES THE MOD CREATES). No network, nothing installed.
    healthbar-overlay.ps1    a short starter script. The mod runs it when the game starts. It builds
                             src\Core.cs into build\DauntlessCompanion.exe with the C# compiler that comes
                             with Windows (part of .NET Framework 4), starts it, and closes.
  The .exe is built on your own PC from the readable src\Core.cs, so you never have to run a program someone
  else compiled. It is only built again when src\Core.cs changes (for example after an update), which is why the
  first start takes a second or two longer.
  The mod doesn't scan the game for behemoths. It waits for your damage numbers to appear on one, then follows
  that behemoth directly until the bar goes away. In Ramsgate and the menu it does nothing.

PERFORMANCE
  The mod is built to be light; on any reasonably modern PC you shouldn't notice it.
  - In the game, each check (4 times a second) takes a fraction of a millisecond.
  - The overlay is a separate program, so a problem in it can never crash the game. A frame takes about 1 ms
    to draw on a typical PC.
  - It redraws only when something changes. Animations redraw 60 times a second (30 with Animation Refresh Rate
    set to 30), but only while they play; the rest of the time the overlay is idle. On a very old CPU, set it to
    30, or turn some animations off or shorten them.
  - While bars are up, healthbar.json (a few hundred bytes) is rewritten at most 4 times a second, and only
    when something changed. With no bars up nothing is written. That has no meaningful effect on an SSD.
  - Build Search works out which perks still fit in the background, so clicking stays smooth.

ABOUT AUTO-START
  To start the overlay, the mod runs one command that launches PowerShell hidden with
  "-ExecutionPolicy Bypass" to run healthbar-overlay.ps1. That flag only applies to that one run; it changes
  no system setting.
  - A command window may flash for a moment when it starts. That comes from how Lua starts programs.
  - Some antivirus tools are suspicious of a game starting hidden PowerShell, or of a new .exe appearing in a
    game folder. If yours blocks it, allow the DauntlessCompanion folder, or turn off Start w/ Game on
    Settings > Overlay (auto_start_overlay = false) and start the overlay yourself (below).
  - Starting the overlay replaces any older copy of it that is still running, so an updated version always
    takes over.
  - You can read every line of main.lua, src\Core.cs and healthbar-overlay.ps1 before using them.

  Starting the overlay yourself:
  - Double-clicking a .ps1 file opens it in Notepad. Instead, right-click healthbar-overlay.ps1 and choose
    "Run with PowerShell", or run this from the mod folder:
        powershell -ExecutionPolicy Bypass -File healthbar-overlay.ps1
    Once the .exe has been built, you can also just double-click build\DauntlessCompanion.exe.
  - Started this way it doesn't close when the game closes. Quit it from its tray icon (right-click > Exit),
    or leave it running: it does nothing while the game is closed.

FILES THE MOD CREATES
  build\ (the overlay program)
  DauntlessCompanion.exe          the overlay, built from src\Core.cs. Safe to delete; it is built again the
                                  next time the game starts.
  DauntlessCompanion.build        a fingerprint of the src\Core.cs the .exe was built from, so an updated
                                  src\Core.cs is always built again. Safe to delete.

  data\ (files the mod writes while it runs)
  settings.ini                    all settings. Recreated with the defaults if you delete it.
  healthbar.json                  the live bar data the mod hands to the overlay. Safe to delete.
  healthbar-position.json         where you moved the bars and how you resized them, and where the settings
                                  window was last put and its size. Delete it (game closed) to reset them.
  healthbar-overlay.log           only when Overlay Log is on.
  healthbar-build.log             why the overlay could not be built, when it couldn't. Safe to delete.
  hunt-stats.ini                  your lifetime hunt stats (only with Hunt Stats on). Reset Stats clears it.
  hunt-stats-player.txt           your player name, so only your own stats are counted (Hunt Stats on).
  hunt-stats-reset.txt            when Reset Stats was last pressed, so stats from before it don't come back.
  saved-builds.txt                the builds you saved.
  pinned-build.txt                the build pinned on screen. Unpinning or the Clear Bars key removes it.
  settings-nav.txt                which groups are folded away in the settings window's left column.
  healthbar-clear.txt             written for a moment when you press the Clear Bars key.
  The mod's own log lines go into UE4SS.log (in the ue4ss folder), tagged [DauntlessCompanion].

TROUBLESHOOTING
  Nothing shows:   open UE4SS.log and search for "[DauntlessCompanion]". You should see "Found local player",
                   then "Now tracking ..." when you hit a behemoth. Then check that the overlay's tray icon is
                   there.
  No tray icon:    look for healthbar-build.log in the data folder. If the overlay could not be built, it says
                   why. The game folder must not be read-only (for example inside Program Files), because the
                   .exe is written to the build folder. Antivirus can also block it (see ABOUT AUTO-START).
  Never shows:     right-click the tray icon: "Foreground app" shows what Windows thinks is in front while you're
                   in the game. If it isn't named Dauntless... or Archon..., add the line
                   "game_process = <that name>" to settings.ini.
  No Hunt Stats:   only hunts you host are counted, from the hunt after Hunt Stats was turned on.
  Owned gear wrong:  press Refresh Inventory on Find Builds. The save is read from
                   %LOCALAPPDATA%\DauntlessForever\versions\1.14.7\data\state.json.
  Overlay problems:  turn on Overlay Log on Settings > Overlay (or set overlay_log = true in settings.ini). The
                   overlay then writes healthbar-overlay.log in the data folder, which is useful when reporting
                   a bug.
  Wrong name:      put the id and the right name in behemoth-names.txt (see NAMES).
  Numpad keys do nothing:  turn Num Lock on, and make sure preview mode (F9) is on.

UNINSTALL
  Switch it off in the launcher (or set "DauntlessCompanion : 0" in Mods\mods.txt), or delete the
  DauntlessCompanion folder. If the overlay is still running, quit it from its tray icon (right-click > Exit);
  otherwise it closes by itself a few seconds after the game closes. Nothing is installed anywhere else.

NOTES
  Built for the Dauntless 1.14.7 package of Dauntless Forever. Other builds may use different class names.
  Hidden server copies of the game also load mods. The bars do nothing there (they need a local player);
  with Hunt Stats on, the hunt's server copy is where your stats are counted.
  Needs Windows 10 or 11. The overlay uses .NET Framework 4, which is part of Windows.

LICENSE
  Dauntless Companion is free software under the GNU Affero General Public License, version 3 (AGPL-3.0-only).
  The full text is in LICENSE. You may use, change and share it, and anything you share that is based on it
  must stay under the same licence with its source available. There is no warranty.

  The icons and pictures in the icons folder use art from Dauntless, which belongs to Phoenix Labs. They
  aren't covered by this licence. Build Search's item data (data\build-data.txt) is based on atomicptr's
  Dauntless Builder data, which is public domain.

  This mod doesn't contain code copied from other projects. It is made to run with:
    - Dauntless Forever (https://github.com/addison2k/DauntlessForever), AGPL-3.0.
    - UE4SS (https://github.com/UE4SS-RE/RE-UE4SS), MIT licence. It is the Lua mod loader this runs on.

NOT AFFILIATED
  This is a fan-made mod. It is not made, endorsed or supported by Forte Labs, Phoenix Labs or Epic Games.
  "Dauntless" and related names belong to their owners. They are used here only to say what this mod works with.
