local HEARTBEAT       = 2.0
local PC_SEARCH_EVERY = 3.0
local LINGER          = 1.5
local MAX_BARS        = 2
local IDLE_DROP       = 30.0
local READ_GRACE      = 2.5
local AUTO_START_OVERLAY = true
local HUNT_STATS = true

local HIT_HOOK = "/Script/Archon.ArchonCombatTextComponent:MulticastSendMultitypeCombatText"
local TICK_CLASS   = "BP_PlayerCharacter_C"
local TICK_HOOK    = "/Game/Blueprints/BP_PlayerCharacter.BP_PlayerCharacter_C:ReceiveTick"
local RESTART_HOOK = "/Script/Engine.PlayerController:ClientRestart"
local TICK_EVERY   = 0.25

local function log(m) print("[DauntlessCompanion] " .. tostring(m) .. "\n") end
local logged = {}
local function logOnce(k, m) if not logged[k] then logged[k] = true log(m) end end
local MP_TEST = false

local function valid(o)
    if not o then return false end
    local ok, v = pcall(function() return o:IsValid() end)
    return ok and v
end

local function nameOf(o)
    local ok, n = pcall(function() return o:GetFullName() end)
    return ok and n or nil
end

local function pathOf(full) return full and full:match("^%S+%s+(.+)$") end

local Dir, DataDir, OutFile

local PC_CLASSES = {"player_controller_bp_C", "ArchonPlayerController", "PlayerController"}

local function isLocal(pc)
    local okP, pl = pcall(function() return pc.Player end)
    if not (okP and valid(pl)) then return false end
    local n = nameOf(pl)
    if not n then return false end
    n = n:lower()
    return (n:find("localplayer", 1, true) or n:find("local_player", 1, true)) and true or false
end

local function findLocalPC()
    for _, cls in ipairs(PC_CLASSES) do
        for _, pc in ipairs(FindAllOf(cls) or {}) do
            if valid(pc) then
                local n = nameOf(pc)
                if not (n and n:find("Default__", 1, true)) and isLocal(pc) then return pc end
            end
        end
    end
    return nil
end

local SETTLE_SECONDS = 5
local settleUntil = os.clock() + SETTLE_SECONDS
local pcCache, lastSearch, diagCount = nil, -100, 0
local everFound, missingSince = false, os.clock()
local serverCopy, serverChecked, serverCheckAt, isClient = false, false, 0, false
local function getPC(now)
    if valid(pcCache) then return pcCache end
    if pcCache ~= nil then
        pcCache = nil
        missingSince = now
        settleUntil = now + SETTLE_SECONDS
        log(string.format("Map changing, pausing searches for %d s", SETTLE_SECONDS))
    end
    if now < settleUntil then return nil end

    if serverCopy then
        if now - serverCheckAt < 120 then return nil end
        serverCheckAt = now
        if #(FindAllOf("LocalPlayer") or {}) == 0 then return nil end
        serverCopy, missingSince, isClient = false, now, true
        log("Local player found, searching again")
    end
    if not everFound and not serverChecked and now - missingSince >= 60 then
        serverChecked = true
        if #(FindAllOf("LocalPlayer") or {}) == 0 then
            serverCopy, serverCheckAt = true, now
            log("No local player after 1 minute; probably a hidden server copy. Pausing search and only checking again every 2 minutes.")
            return nil
        end
        isClient = true
    end

    local every = PC_SEARCH_EVERY
    if not everFound and not isClient then
        local missing = now - missingSince
        if missing >= 120 then every = 30 elseif missing >= 30 then every = 10 end
    end
    if now - lastSearch < every then return nil end
    lastSearch = now
    pcCache = findLocalPC()
    if pcCache then
        everFound = true
        settleUntil = now + SETTLE_SECONDS
        log(string.format("Found local player (%s). Giving the map %d s to finish loading.", nameOf(pcCache) or "?", SETTLE_SECONDS))
    elseif diagCount < 2 then
        diagCount = diagCount + 1
        log("No local player in this copy of the game yet: normal for a hidden server copy.")
    end
    return pcCache
end

local function fileExists(path)
    local f = io.open(path, "r")
    if f then f:close() return true end
    return false
end

local function modFolder()
    local function scriptsParent(p)
        if not p then return nil end
        p = p:gsub("^@", "")
        local dir = p:match("^(.*)[/\\][Ss][Cc][Rr][Ii][Pp][Tt][Ss][/\\][^/\\]*$")
        if dir and dir:match("^%a:[/\\]") then return dir end
        return nil
    end
    local ok, info = pcall(function() return debug.getinfo(1, "S") end)
    if ok and info then
        local d = scriptsParent(info.source)
        if d and fileExists(d .. "\\healthbar-overlay.ps1") then return d end
    end
    for entry in tostring(package.path):gmatch("[^;]+") do
        local dir = entry:match("^(.*)[/\\][Ss][Cc][Rr][Ii][Pp][Tt][Ss][/\\]%?%.lua$")
        if dir and dir:match("^%a:[/\\]") and fileExists(dir .. "\\healthbar-overlay.ps1") then return dir end
    end
    return nil
end

Dir = modFolder()
DataDir = Dir and (Dir .. "\\data")
OutFile = DataDir and (DataDir .. "\\healthbar.json")

local function dataDirReady()
    if not DataDir then return false end
    local probe = DataDir .. "\\.write-test"
    local f = io.open(probe, "w")
    if not f then return false end
    f:close()
    os.remove(probe)
    return true
end

local function setUpDataDir()
    if not DataDir then return end
    if not dataDirReady() and type(os.execute) == "function" and not DataDir:find('"', 1, true) then
        pcall(os.execute, 'mkdir "' .. DataDir .. '" 2>nul')
    end
    if not dataDirReady() then
        log("Couldn't write to the data folder. Check the mod folder isn't read-only")
        return
    end
    local moved = {}
    for _, name in ipairs({"hunt-stats.ini", "hunt-stats-player.txt", "settings.ini"}) do
        local old, new = Dir .. "\\" .. name, DataDir .. "\\" .. name
        if fileExists(old) and not fileExists(new) and os.rename(old, new) then moved[#moved + 1] = name end
    end
    for _, name in ipairs({"healthbar.json", "healthbar-clear.txt", "hunt-stats.ini.tmp"}) do
        if fileExists(Dir .. "\\" .. name) then os.remove(Dir .. "\\" .. name) end
    end
    if #moved > 0 then log("Moved old files to the data folder: " .. table.concat(moved, ", ")) end
end
local okD, errD = pcall(setUpDataDir)
if not okD then log("Data folder setup failed: " .. tostring(errD)) end

local DEFAULT_SETTINGS = [==[
; LIVE settings update as soon as you save this file. RESTART settings apply the next time you launch the game.
;
; SETTINGS WINDOW: press F10 in game (or double-click the overlay's icon in the system tray) to change all of these
; in a window instead. It saves to this file for you.
;
; MOVING AND RESIZING: press F9 in game to turn on preview mode. While it's on, the move and resize keys below
; (Ctrl+Alt+Arrows and Ctrl+Shift+Arrows by default, + Home resets) move and resize the bars. Press F9 again when you're done.
;
; PERFORMANCE: the animations redraw the bars about 60 times a second while they play (30 with animation_fps = 30).
; That's unnoticeable on almost any PC, but if you're on a very old CPU and notice slowdowns, set animation_fps to
; 30, or turn some of the animations off or shorten them.

[Keybinds]  ; LIVE unless marked RESTART
; Any key, alone or with Ctrl/Alt/Shift (e.g. Ctrl+F8). Number row = 1 to 0, number pad = NumPad0 to NumPad9 (Num Lock on).
settings_key = F10              ; any key              | default F10     | open the settings window
hide_key = F8                   ; any key              | default F8      | show / hide the bars
clear_key = F4                  ; any key              | default F4      | clear all active bars
preview_mode_key = F9           ; any key              | default F9      | preview mode on / off
preview_enrage_key = NumPad1    ; any key              | default NumPad1 | preview mode: enrage glow on / off
preview_aether_key = NumPad2    ; any key              | default NumPad2 | preview mode: aethercharge glow on / off
preview_hit_key = NumPad3       ; any key              | default NumPad3 | preview mode: take a big hit
preview_kill_key = NumPad4      ; any key              | default NumPad4 | preview mode: kill / revive the bars
move_up_key = Ctrl+Alt+Up       ; any key              | default Ctrl+Alt+Up | preview mode: move the bars up
move_down_key = Ctrl+Alt+Down   ; any key              | default Ctrl+Alt+Down | preview mode: move the bars down
move_left_key = Ctrl+Alt+Left   ; any key              | default Ctrl+Alt+Left | preview mode: move the bars left
move_right_key = Ctrl+Alt+Right ; any key              | default Ctrl+Alt+Right | preview mode: move the bars right
reset_position_key = Ctrl+Alt+Home ; any key              | default Ctrl+Alt+Home | preview mode: put the bars back in the middle
taller_key = Ctrl+Shift+Up      ; any key              | default Ctrl+Shift+Up | preview mode: make the bars taller
shorter_key = Ctrl+Shift+Down   ; any key              | default Ctrl+Shift+Down | preview mode: make the bars shorter
wider_key = Ctrl+Shift+Right    ; any key              | default Ctrl+Shift+Right | preview mode: make the bars wider
narrower_key = Ctrl+Shift+Left  ; any key              | default Ctrl+Shift+Left | preview mode: make the bars narrower
reset_size_key = Ctrl+Shift+Home ; any key              | default Ctrl+Shift+Home | preview mode: put the bars back to normal size

[Health Bar]  ; LIVE unless marked RESTART
max_bars = 2                    ; RESTART. 1 to 4      | default 2       | max amount of bars shown
health_numbers = true           ; true, false          | default true    | health amount inside the bar
health_percent = true           ; true, false          | default true    | health % next to the amount
number_format = full            ; full, condensed      | default full    | full (123,456,789) or condensed, cut not rounded (123.4M)
name_align = center             ; left, center, right  | default center  | where the behemoth name sits above the bar
name_offset = 0                 ; 0 to 20              | default 0       | moves the behemoth name down toward the bar
quarter_marks = true            ; true, false          | default true    | faint lines at each quarter
health_color = #c42824          ; #rrggbb or r, g, b   | default #c42824 | health bar fill color
name_color = #ffffff            ; #rrggbb or r, g, b   | default #ffffff | behemoth name color
number_color = #ffffff          ; #rrggbb or r, g, b   | default #ffffff | health numbers color
idle_bar_seconds = 30           ; RESTART. 3 to 3600   | default 30      | how long an undamaged bar stays up
dead_bar_seconds = 1.5          ; RESTART. 0 to 60     | default 1.5     | how long a bar stays after the behemoth dies

[Stagger Bar]  ; LIVE // A strip under the health bar that fills as you build stagger.
stagger_bar = true              ; true, false          | default true    | show the stagger bar
stagger_color = #42eff5         ; #rrggbb or r, g, b   | default #42eff5 | stagger bar color
divider = true                  ; true, false          | default true    | thin line between health and stagger bar
divider_color = #06080c         ; #rrggbb or r, g, b   | default #06080c | divider color
divider_thickness = 1           ; auto, 1 to 10        | default 1       | divider thickness in pixels

[Enrage & Aethercharge]  ; LIVE // A glow along the top of the bar while the behemoth is enraged or aethercharged.
enrage_glow = true              ; true, false          | default true    | glow while enraged
enrage_glow_color = #dc0000     ; #rrggbb or r, g, b   | default #dc0000 | enrage glow color
aether_glow = true              ; true, false          | default true    | glow while aethercharged
aether_glow_color = #03bbda     ; #rrggbb or r, g, b   | default #03bbda | aethercharge glow color
glow_style = line               ; glow, line           | default line    | soft glow, or a crisp line on top
glow_size = auto                ; auto, 2 to 40        | default auto    | how far the glow reaches, in pixels
glow_strength = 0.9             ; 0.1 to 1             | default 0.9     | glow brightness

[Animations]  ; LIVE
animation_fps = 60              ; 30, 60               | default 60      | how smooth the animations are (60 uses a bit more CPU while they play)

; Damage trail: highlights the chunk of health a big hit took off, then shrinks it away.
damage_trail = true             ; true, false          | default true    | show the damage trail
damage_trail_color = #f5c76b    ; #rrggbb or r, g, b   | default #f5c76b | damage trail color
damage_trail_min = 7.5          ; 7.5 to 100           | default 7.5     | % of max health a hit needs to show the trail

; Death: the empty bar shrinks in from both ends and the name fades out.
death_shrink = true             ; true, false          | default true    | show the death animation
death_shrink_seconds = 0.7      ; 0.1 to 2             | default 0.7     | how long the animation lasts

; Glow sweep: how the enrage / aethercharge glow comes and goes.
glow_animation = true           ; true, false          | default true    | sweep the glow in instead of popping on
glow_animation_out = true       ; true, false          | default true    | sweep the glow out when the state ends
glow_animation_seconds = 0.6    ; 0.1 to 3             | default 0.6     | how long the sweep animation takes

[Layout]  ; LIVE // Size and position are set in game with preview mode (F9).
stack_upward = auto             ; auto, true, false    | default auto    | new bars above (true), below (false), auto picks
side_by_side = false            ; true, false          | default false   | bars side by side instead of stacked
gap = auto                      ; auto, 0 to 200       | default auto    | space between bars, in pixels
slant = 0.45                    ; 0 to 1               | default 0.45    | how angled the bar ends are (0 = square)

[Health Bar Settings]  ; LIVE
health_bars = true              ; true, false          | default true    | show health bars for the behemoths in your hunt
settings_preview = true         ; true, false          | default true    | show test bars while the settings window is on a Health Bars page

[Overlay Settings]  ; LIVE unless marked RESTART
auto_start_overlay = true       ; RESTART. true, false | default true    | start the overlay with the game
show_when_not_focused = false   ; true, false          | default false   | show bars when the game isn't in front
overlay_theme = torgadoro       ; custom, torgadoro, malkarion, urska, thrax, agarus, chronovore | default torgadoro | sets theme, pill and hover colors from a keystone behemoth
theme_color = #fd7a44           ; hex color            | default #fd7a44 | color of the accent text and highlights in the settings window
pill_color = #6c1025            ; hex color            | default #6c1025 | color of the selected pills and tabs in the settings window
hover_color = #fdb836           ; hex color            | default #fdb836 | outline color of tiles and swatches under the mouse in the settings window
theme_icon = torgadoro          ; torgadoro, malkarion, urska, thrax, agarus, chronovore | default torgadoro | settings window icon (stays when a color change switches the theme to custom)
heading_size = 10               ; 8 to 16              | default 10      | size of the group headings (Format, Colors) in the settings window
close_on_click_outside = true   ; true, false          | default true    | close the settings window on an outside click
overlay_log = false             ; true, false          | default false   | write healthbar-overlay.log (only needed for troubleshooting)


[Hunt Stats Settings]  ; LIVE unless marked RESTART
hunt_stats = true               ; true, false          | default true    | keep lifetime stats per weapon type (from your next hunt)
stats_layout = tiles            ; tiles, list          | default tiles   | Hunt Stats page as tiles or as a list


[Build Search Settings]  ; LIVE
build_search = true             ; true, false          | default true    | Build Search page: find armour sets for the perks you want
weapon_layout = tiles           ; tiles, list          | default tiles   | Build Search weapons as picture tiles or a plain list
build_count = 5                 ; 1 to 25              | default 5       | builds listed at first on Find Builds (Show More adds this many)
owned_only = false              ; true, false          | default false   | only find builds where you own the weapon, every armour piece and every cell
cell_ranks = 3_6                ; 3_6, all             | default 3_6     | perk descriptions list +3 and +6 only, or every rank +1 to +6
pinned_build_position = left    ; left, right, corners | default left    | where a pinned saved build shows (corners: top_left, top_right, bottom_left, bottom_right)
pinned_build_opacity = 96       ; 0 to 100             | default 96      | how solid the pinned build background is (text and icons stay solid)
]==]

local function writeDefaultSettings()
    local base = DataDir
    if not base then return end
    if fileExists(base .. "\\settings.ini") then return end
    local f = io.open(base .. "\\settings.ini", "w")
    if not f then log("Couldn't find settings.ini and couldn't create a new one, so the defaults are being used") return end
    f:write(DEFAULT_SETTINGS)
    f:close()
    log("settings.ini was missing, fresh one with defaults created")
end
local okW, errW = pcall(writeDefaultSettings)
if not okW then log("Something went wrong making settings.ini: " .. tostring(errW)) end

local function loadSettings()
    local base = DataDir
    local f = base and io.open(base .. "\\settings.ini", "r")
    if not f then log("No settings.ini found, using defaults") return end
    local s = {}
    for line in f:lines() do
        local t = line:gsub("^%s+", ""):gsub("%s+$", "")
        local first = t:sub(1, 1)
        if t ~= "" and first ~= "#" and first ~= ";" and first ~= "[" then
            local k, v = t:match("^([^=]-)%s*=%s*(.*)$")
            if k and v then
                local cut = v:find(";", 1, true) or v:find("#%s") or v:find("#$")
                if cut then v = v:sub(1, cut - 1) end
                v = v:gsub("%s+$", "")
                if k ~= "" and v ~= "" then s[k:lower()] = v end
            end
        end
    end
    f:close()

    local function num(key, cur, lo, hi)
        local v = s[key]
        if not v then return cur end
        local n = tonumber(v)
        if not n then log(string.format('settings.ini: "%s = %s" isn\'t a number, so I\'m using %s instead', key, v, tostring(cur))) return cur end
        return math.max(lo, math.min(hi, n))
    end
    MAX_BARS  = math.floor(num("max_bars", MAX_BARS, 1, 4))
    LINGER    = num("dead_bar_seconds", LINGER, 0, 60)
    IDLE_DROP = num("idle_bar_seconds", IDLE_DROP, 3, 3600)

    local a = s["auto_start_overlay"] and s["auto_start_overlay"]:lower()
    if a == "on" or a == "true" or a == "yes" or a == "auto" then AUTO_START_OVERLAY = true
    elseif a == "off" or a == "false" or a == "no" then AUTO_START_OVERLAY = false
    elseif a then log('settings.ini: auto_start_overlay has to be true or false, default: true') AUTO_START_OVERLAY = true end

    local hs = s["hunt_stats"] and s["hunt_stats"]:lower()
    if hs == "on" or hs == "true" or hs == "yes" then HUNT_STATS = true
    elseif hs == "off" or hs == "false" or hs == "no" then HUNT_STATS = false
    elseif hs then log('settings.ini: hunt_stats has to be true or false, default: true') HUNT_STATS = true end

    log(string.format("Settings loaded: max_bars %d, dead_bar_seconds %s, idle_bar_seconds %s, auto_start_overlay %s, hunt_stats %s",
        MAX_BARS, tostring(LINGER), tostring(IDLE_DROP), tostring(AUTO_START_OVERLAY), tostring(HUNT_STATS)))
end
local okS, errS = pcall(loadSettings)
if not okS then log("Something went wrong reading settings.ini: " .. tostring(errS) .. " (falling back to the defaults)") end

local overlayTried = false
local function startOverlay()
    local want = AUTO_START_OVERLAY
    if overlayTried or not want then return end
    overlayTried = true
    if type(os.execute) ~= "function" then
        log("Can't start the overlay automatically here. Run healthbar-overlay.ps1 yourself.")
        return
    end
    local base = modFolder()
    if not base then
        log("Couldn't find healthbar-overlay.ps1 next to Scripts folder. Launch the file manually.")
        return
    end
    local script = base .. "\\healthbar-overlay.ps1"
    local outDir = DataDir
    if script:find('"', 1, true) or (outDir and outDir:find('"', 1, true)) then
        log("Didn't auto-start the overlay because a folder path has unusual characters in it")
        return
    end
    local cmd = 'start "" conhost.exe powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' .. script .. '" -QuitWhenGameCloses'
    if outDir and outDir ~= "" then cmd = cmd .. ' -Dir "' .. outDir .. '"' end
    log("Starting the overlay: " .. cmd)
    local ok, err = pcall(os.execute, cmd)
    if not ok then log("Couldn't start the overlay: " .. tostring(err)) end
end

local function playerStateOf(src)
    if not valid(src) then return nil end
    local ok, ps = pcall(function() return src.PlayerState end)
    if ok and valid(ps) then return ps end
    for _, field in ipairs({"Instigator", "Owner"}) do
        local ok2, o = pcall(function() return src[field] end)
        if ok2 and valid(o) then
            local ok3, ps2 = pcall(function() return o.PlayerState end)
            if ok3 and valid(ps2) then return ps2 end
        end
    end
    return nil
end

local function addressOf(o)
    local ok, a = pcall(function() return o:GetAddress() end)
    return ok and a or nil
end

local function localPSAddress()
    if not valid(pcCache) then return nil end
    local ok, ps = pcall(function() return pcCache.PlayerState end)
    if ok and valid(ps) then return addressOf(ps) end
    return nil
end

local hitQueue = {}
local function onHits(entries, comp)
    local mine = localPSAddress()
    if not mine then return end
    local hitMine = false
    entries:ForEach(function(i, elem)
        if hitMine then return true end
        local ps = playerStateOf(elem:get().SourceActor)
        if ps and addressOf(ps) == mine then
            hitMine = true
            return true
        end
    end)
    if hitMine and comp and #hitQueue < 32 then hitQueue[#hitQueue + 1] = comp end
end

local attrSets, attrMissAt = {}, {}

local classIsBehemoth = {}
local function behemothByClass(obj)
    local okC, cls = pcall(function() return obj:GetClass() end)
    if not (okC and valid(cls)) then return nil end
    local cname = nameOf(cls)
    if cname and classIsBehemoth[cname] ~= nil then return classIsBehemoth[cname] end
    local result = false
    local ok = pcall(function()
        local c = cls
        for _ = 1, 40 do
            if not valid(c) then break end
            if c:GetFName():ToString() == "ArchonBehemoth" then result = true break end
            c = c:GetSuperStruct()
        end
    end)
    if not ok then return nil end
    if cname then classIsBehemoth[cname] = result end
    return result
end

local behemothByName = {}
local function behemothByList(obj)
    local n = nameOf(obj)
    if not n then return false end
    if behemothByName[n] ~= nil then return behemothByName[n] end
    logOnce("classcheck", "Couldn't read class info, hits are being checked against the behemoth list instead")
    local found = false
    for _, b in ipairs(FindAllOf("ArchonBehemoth") or {}) do
        if nameOf(b) == n then found = true break end
    end
    behemothByName[n] = found
    return found
end

local function hitBehemoth(comp)
    local okO, outer = pcall(function() return comp:GetOuter() end)
    if not (okO and valid(outer)) then return nil end
    local list = {outer}
    local okW, owner = pcall(function() return outer.Owner end)
    if okW and valid(owner) then list[#list + 1] = owner end
    for _, o in ipairs(list) do
        local r = behemothByClass(o)
        if r == nil then r = behemothByList(o) end
        if r then return o end
    end
    return nil
end

local function readNum(obj, prop)
    local ok, v = pcall(function() return obj[prop] end)
    if ok and type(v) == "number" then return v end
    return nil
end

local function directHealth(b)
    local okA, asc = pcall(function() return b.AbilitySystemComponent end)
    if not (okA and valid(asc)) then return nil end
    local found = nil
    pcall(function()
        asc.SpawnedAttributes:ForEach(function(i, elem)
            if not found then
                local o = elem:get()
                local n = valid(o) and nameOf(o)
                if n and n:find("^ArchonHealthAttributeSet ") then found = o end
            end
        end)
    end)
    return found
end

local function attrFor(key, b)
    local a = attrSets[key]
    if valid(a) then return a end
    attrSets[key] = nil
    if b then
        local d = directHealth(b)
        if d and (readNum(d, "MaxHealth") or 0) > 0 then attrSets[key] = d attrMissAt[key] = nil return d end
        logOnce("nodirecthealth:" .. key, "Health isn't linked on " .. (key:match("[^%.]+$") or key) .. ", looking for it another way")
    end
    local now = os.clock()
    if attrMissAt[key] and now - attrMissAt[key] < 2 then return nil end
    local path = pathOf(key)
    if not path then return nil end
    local best, bestMax = nil, -1
    for _, s in ipairs(FindAllOf("ArchonHealthAttributeSet") or {}) do
        if valid(s) then
            local sp = pathOf(nameOf(s))
            if sp and sp:sub(1, #path + 1) == path .. "." then
                local m = readNum(s, "MaxHealth") or 0
                if m > bestMax then best, bestMax = s, m end
            end
        end
    end
    if best then attrSets[key] = best attrMissAt[key] = nil else attrMissAt[key] = now end
    return best
end

local staggers, staggerMissAt = {}, {}

local noStagger = {}
local function staggerFor(key, b)
    local s = staggers[key]
    if valid(s) then return s end
    staggers[key] = nil
    if noStagger[key] then return nil end
    if b then
        local okS, d = pcall(function() return b.staggerable_bpc end)
        if okS and d ~= nil then
            if valid(d) then staggers[key] = d staggerMissAt[key] = nil return d end
            noStagger[key] = true
            return nil
        end
        logOnce("nodirectstagger:" .. key, "Stagger isn't linked on " .. (key:match("[^%.]+$") or key) .. ", looking for it another way")
    end
    local now = os.clock()
    if staggerMissAt[key] and now - staggerMissAt[key] < 5 then return nil end
    local path = pathOf(key)
    if not path then return nil end
    for _, c in ipairs(FindAllOf("staggerable_bpc_C") or {}) do
        if valid(c) then
            local cp = pathOf(nameOf(c))
            if cp and cp:sub(1, #path + 1) == path .. "." then
                staggers[key] = c
                staggerMissAt[key] = nil
                return c
            end
        end
    end
    staggerMissAt[key] = now
    return nil
end

local function readBool(obj, prop)
    local ok, v = pcall(function() return obj[prop] end)
    if ok and type(v) == "boolean" then return v end
    return nil
end

local function updateStagger(e, key, now, b)
    local c = staggerFor(key, b)
    local cur = c and readNum(c, "CurrentStaggerHealth")
    local mx = c and readNum(c, "CurrentStaggerMaxHealth")
    if not (cur and mx and mx > 0 and mx < 1e7) or readBool(c, "FinishedStaggerStages") then
        e.sgFill, e.sgMax, e.sgHoldAt = 0, 0, nil
        return
    end
    cur = math.max(0, math.min(cur, mx))
    local stage = readNum(c, "CurrentStaggerStage")
    local stunned = stage and e.sgStage and stage > e.sgStage
    if stunned and not e.sgHoldAt then
        e.sgHoldAt = now
        log(string.format("%s is stunned (stagger stage %s, %.0f / %.0f)", tostring(e.id), tostring(stage), cur, mx))
    end
    if stage then e.sgStage = stage end

    if e.sgHoldAt then
        local last = e.sgLast or cur
        local reset = cur >= mx - 0.5
        local hitAgain = cur > 0.5 and cur < last - math.max(1, mx * 0.01)
        if (reset or hitAgain) and now - e.sgHoldAt > 0.6 then
            log(string.format("%s is building stagger again after %.1f s (%s)", tostring(e.id), now - e.sgHoldAt,
                reset and "count reset" or "stagger landed"))
            e.sgHoldAt = nil
        end
    end
    e.sgLast = cur
    e.sgMax = mx
    e.sgFill = e.sgHoldAt and 0 or (1 - cur / mx)
end

local function idOf(key)
    local cls = (key and key:match("^(%S+)")) or "behemoth"
    cls = cls:lower():gsub("_c$", ""):gsub("_bp$", "")
    return (cls:gsub("[^%w_]", ""))
end

local function prettyName(id)
    local n = id:gsub("_beta$", ""):gsub("_", " ")
    n = n:gsub("(%a)(%w*)", function(a, b) return a:upper() .. b end)
    return (n:gsub("[^%w _%-]", ""))
end

local entries, order = {}, {}
local lastKnown = {}
local engCount = 0
local START_TRAIL_SECONDS = 1.5
local tracked = {}

local function isClone(id) return ("_" .. tostring(id) .. "_"):find("_clone_", 1, true) ~= nil end

local function oldestBar(clonesOnly)
    local oldest, oldestAt = nil, math.huge
    for _, k in ipairs(order) do
        local e = entries[k]
        if e and (not clonesOnly or isClone(e.id)) then
            local at = e.changedAt or 0
            if at < oldestAt then oldest, oldestAt = k, at end
        end
    end
    return oldest
end

local drop
local function engage(key, hp, mx)
    local id = idOf(key)
    local clone = isClone(id)
    while #order >= MAX_BARS do
        local victim = oldestBar(true)
        if not victim and not clone then victim = oldestBar(false) end
        if not victim then
            logOnce("noslot:" .. key, "Max bars reached, no room for " .. id)
            return
        end
        log("Max bars reached, dropping the one that's been quiet the longest (" .. tostring(idOf(victim)) .. ")")
        drop(victim)
    end
    local from = lastKnown[key] or mx
    lastKnown[key] = nil
    entries[key] = {key = key, id = id, name = prettyName(id),
                    hp = hp, mx = mx, shield = 0, peak = 0, deadAt = nil, changedAt = os.clock(),
                    from = from, engagedAt = os.clock(), eng = engCount + 1}
    engCount = engCount + 1
    order[#order + 1] = key
    log(string.format("Now tracking %s (%.0f / %.0f health)", id, hp, mx))
end

drop = function(key)
    local e = entries[key]
    if e and not e.deadAt and e.hp > 0 then lastKnown[key] = e.hp end
    entries[key] = nil
    for i = #order, 1, -1 do
        if order[i] == key then table.remove(order, i) end
    end
end

local lastBody, lastWriteAt = nil, -100
local function writeBars(now)
    if not OutFile then logOnce("nodir", "Unable to locate mod folder, nothing will be written") return end
    local shown = {}
    for _, wantClone in ipairs({false, true}) do
        for i = math.max(1, #order - MAX_BARS + 1), #order do
            local e = entries[order[i]]
            if e and isClone(e.id) == wantClone then shown[#shown + 1] = e end
        end
    end
    local list = {}
    for _, e in ipairs(shown) do
        do
            list[#list + 1] = string.format(
                '{"id":"%s","name":"%s","hp":%.0f,"max":%.0f,"shield":%.0f,"shieldMax":%.0f,"sgFill":%.3f,"sgMax":%.0f,"rage":%d,"aether":%d,"from":%.0f,"eng":%d}',
                e.id, e.name, math.max(e.hp, 0), e.mx, e.shield, e.peak, e.sgFill or 0, e.sgMax or 0,
                e.rage and 1 or 0, e.aether and 1 or 0,
                (e.from and e.engagedAt and now - e.engagedAt < START_TRAIL_SECONDS) and e.from or 0, e.eng or 0)
        end
    end
    local body = table.concat(list, ",")
    if body == lastBody and (#list == 0 or now - lastWriteAt < HEARTBEAT) then return end
    local f = io.open(OutFile, "w")
    if not f then logOnce("openfail", "Couldn't write to " .. OutFile) return end
    f:write(string.format('{"v":2,"time":%d,"bars":[%s]}', os.time(), body))
    f:close()
    lastBody, lastWriteAt = body, now
    logOnce("writing", "Writing bars to " .. OutFile)
end

local function updateEntry(key, b, hp, mx, s, now)
    local e = entries[key]
    if e then
        if hp < e.hp - 0.01 or (e.rawShield ~= nil and s < e.rawShield - 0.01) then e.changedAt = now end
        e.rawShield = s
        e.hp, e.mx = hp, mx
        if s > 0.5 then
            e.shield = s
            e.peak = math.max(e.peak, s)
        else
            e.shield, e.peak = 0, 0
        end
        if hp <= 0 and not e.deadAt then e.deadAt = now end
        updateStagger(e, key, now, b)
        e.rage = hp > 0 and readBool(b, "InRageState") == true
        e.aether = hp > 0 and readBool(b, "AetherCharged") == true
    end
end

local copyChecked, playedCopy = false, false
local function isPlayedCopy()
    if not copyChecked then
        copyChecked = true
        local ok, n = pcall(function() return #(FindAllOf("LocalPlayer") or {}) end)
        playedCopy = ok and n ~= nil and n > 0
        if not playedCopy then log("No local player in this copy; stopping bar updates here")
        elseif MP_TEST then log("MP test: this copy has a local player (your game window)") end
    end
    return playedCopy
end

local StatsFile = DataDir and (DataDir .. "\\hunt-stats.ini")
local PlayerFile = DataDir and (DataDir .. "\\hunt-stats-player.txt")
local STAT_FLUSH_EVERY = 5
local WEAPON_KEYS = {"Hammer", "Axe", "Sword", "ChainBlades", "WarPike", "Repeaters", "AetherStrikers", "Bow"}
local STAT_KEYS = {"damage", "core_damage", "part_damage", "stagger_damage", "wound_damage", "biggest_hit", "kills", "fauna_kills", "gatherables", "patrol_chests",
                   "parts_broken", "staggers", "interrupts", "wounds", "perfect_dodges", "deaths", "bounties",
                   "heal_received", "damage_taken", "procs", "procs_blaze", "procs_frost", "procs_shock",
                   "procs_radiant", "procs_umbral", "procs_terra"}
local STAT_IS_MAX = {biggest_hit = true}
local KILLS_SECTION = "Behemoths"

local function weaponKeyOf(ps)
    local ok, w = pcall(function() return ps.Weapon end)
    if ok and type(w) == "number" and WEAPON_KEYS[w] then return WEAPON_KEYS[w] end
    return "Unknown"
end

local function isPlayerState(o)
    local okC, cls = pcall(function() return o:GetClass():GetFName():ToString() end)
    if not okC or not cls then return false end
    cls = cls:lower()
    return cls:find("player_state", 1, true) ~= nil or cls:find("playerstate", 1, true) ~= nil
end

local function statsPlayerState(obj)
    local o = obj
    for _ = 1, 8 do
        if not valid(o) then return nil end
        if isPlayerState(o) then return o end
        local okP, ps = pcall(function() return o.PlayerState end)
        if okP and valid(ps) then return ps end
        local okO, outer = pcall(function() return o:GetOuter() end)
        if not okO then return nil end
        o = outer
    end
    return nil
end

local function playerNameOf(ps)
    local ok, n = pcall(function() return ps.PlayerNamePrivate:ToString() end)
    if ok and type(n) == "string" and n ~= "" then return n end
    return nil
end

local writtenName, nameCheckAt = nil, -100
local function notePlayerName(pc, now)
    if not PlayerFile or now - nameCheckAt < 10 then return end
    nameCheckAt = now
    local okP, ps = pcall(function() return pc.PlayerState end)
    local name = okP and valid(ps) and playerNameOf(ps)
    if not name or name == writtenName then return end
    local f = io.open(PlayerFile, "w")
    if not f then logOnce("statsname", "Hunt stats: couldn't write " .. PlayerFile) return end
    f:write(name)
    f:close()
    writtenName = name
    log("Hunt stats: saved your player name so only your own stats are counted")
end

local myName, myNameAt = nil, -100
local isMeCache = {}
local function isMe(ps)
    local now = os.clock()
    if now - myNameAt > (myName and 30 or 5) then
        myNameAt = now
        local f = PlayerFile and io.open(PlayerFile, "r")
        local n = f and f:read("*l")
        if f then f:close() end
        n = n and n:gsub("^%s+", ""):gsub("%s+$", "")
        if n == "" then n = nil end
        myName, isMeCache = n, {}
    end
    if not myName then return false end
    local key = tostring(addressOf(ps) or 0) .. ":" .. tostring(readNum(ps, "PlayerId") or "")
    local cached = isMeCache[key]
    if cached ~= nil then return cached end
    local name = playerNameOf(ps)
    local result = (name ~= nil) and (name == myName)
    if name ~= nil then isMeCache[key] = result end
    return result
end

local function readStatsFile()
    local t = {}
    if not StatsFile then return t end
    if not fileExists(StatsFile) and fileExists(StatsFile .. ".bak") then os.rename(StatsFile .. ".bak", StatsFile) end
    local f, err, code = io.open(StatsFile, "r")
    if not f then
        if code == 2 then return t end
        return nil, err
    end
    local sec
    for line in f:lines() do
        local name = line:match("^%s*%[([^%]]+)%]")
        if name then
            sec = name
            t[sec] = t[sec] or {}
        elseif sec then
            local k, v = line:match("^%s*([%w_]+)%s*=%s*([%d%.]+)")
            if k then t[sec][k] = tonumber(v) end
        end
    end
    f:close()
    return t
end

local pendingStats, statsDirty, lastStatsFlush = {}, false, 0
local pendingSince = nil
local ResetFile = DataDir and (DataDir .. "\\hunt-stats-reset.txt")
local handledReset = 0
local COPY_ID = (tostring({}):gsub("%W", "")) .. tostring(os.time())
local LockFile = StatsFile and (StatsFile .. ".lock")

local function lockStats()
    local mine = StatsFile .. "." .. COPY_ID .. ".lk"
    for attempt = 1, 2 do
        local f = io.open(mine, "w")
        if not f then return false end
        f:write(tostring(os.time()))
        f:close()
        if os.rename(mine, LockFile) then return true end
        os.remove(mine)
        local lf = io.open(LockFile, "r")
        local at = lf and tonumber(lf:read("*l") or "")
        if lf then lf:close() end
        if attempt == 2 or (at and os.time() - at <= 30) then return false end
        os.remove(LockFile)
    end
    return false
end

local waitFailedAt = -100
local function waitLock(force)
    local locked = lockStats()
    if locked or not force then return locked end
    if os.clock() - waitFailedAt < 30 then return false end
    local stopAt = os.clock() + 0.05
    while not locked and os.clock() < stopAt do
        local nextTry = os.clock() + 0.005
        while os.clock() < nextTry do end
        locked = lockStats()
    end
    if not locked then waitFailedAt = os.clock() end
    return locked
end

local function flushStats(force)
    if not statsDirty or not StatsFile then return end
    local now = os.clock()
    if not force and now - lastStatsFlush < STAT_FLUSH_EVERY then return end
    lastStatsFlush = now
    if not waitLock(force) then return end
    local function unlock() os.remove(LockFile) end
    if ResetFile then
        local rf = io.open(ResetFile, "r")
        if rf then
            local resetAt = tonumber(((rf:read("*l") or ""):match("%d+")))
            rf:close()
            if resetAt and resetAt > handledReset then
                handledReset = resetAt
                if pendingSince and resetAt >= pendingSince then
                    pendingStats, statsDirty, pendingSince = {}, false, nil
                    unlock()
                    log("Hunt stats reset; unsaved stats have been discarded")
                    return
                end
            end
        end
    end
    local t, readErr = readStatsFile()
    if not t then
        unlock()
        logOnce("statsread", "Hunt stats: couldn't read " .. StatsFile .. " (" .. tostring(readErr) .. "), keeping the new stats to save later")
        return
    end
    for w, p in pairs(pendingStats) do
        t[w] = t[w] or {}
        for k, v in pairs(p) do
            if STAT_IS_MAX[k] then t[w][k] = math.max(t[w][k] or 0, v)
            else t[w][k] = (t[w][k] or 0) + v end
        end
    end
    local kt = t[KILLS_SECTION] or {}
    t[KILLS_SECTION] = nil
    for _, s in pairs(t) do
        for k, v in pairs(s) do
            if k:sub(1, 5) == "kill_" then kt[k] = (kt[k] or 0) + v s[k] = nil end
        end
        s.damage = (s.core_damage or 0) + (s.part_damage or 0) + (s.stagger_damage or 0) + (s.wound_damage or 0)
    end
    local order = {}
    for _, w in ipairs(WEAPON_KEYS) do if t[w] then order[#order + 1] = w end end
    local extra = {}
    for w in pairs(t) do
        local known = false
        for _, k in ipairs(WEAPON_KEYS) do if k == w then known = true break end end
        if not known then extra[#extra + 1] = w end
    end
    table.sort(extra)
    for _, w in ipairs(extra) do order[#order + 1] = w end
    local out = {"; Dauntless Companion - lifetime hunt stats, one section per weapon type.",
                 "; Written by the mod while Hunt Stats is on. Delete this file (game closed) to reset your stats.", ""}
    for _, w in ipairs(order) do
        out[#out + 1] = "[" .. w .. "]"
        for _, k in ipairs(STAT_KEYS) do out[#out + 1] = string.format("%s = %.0f", k, t[w][k] or 0) end
        local wk = {}
        for k, v in pairs(t[w]) do if k:sub(1, 6) == "wkill_" and v > 0 then wk[#wk + 1] = k end end
        table.sort(wk)
        for _, k in ipairs(wk) do out[#out + 1] = string.format("%s = %.0f", k, t[w][k]) end
        out[#out + 1] = ""
    end
    local kills = {}
    for k, v in pairs(kt) do if v > 0 then kills[#kills + 1] = k end end
    if #kills > 0 then
        table.sort(kills)
        out[#out + 1] = "[" .. KILLS_SECTION .. "]"
        for _, k in ipairs(kills) do out[#out + 1] = string.format("%s = %.0f", k, kt[k]) end
        out[#out + 1] = ""
    end
    local tmp = StatsFile .. "." .. COPY_ID .. ".tmp"
    local f = io.open(tmp, "w")
    if not f then unlock() logOnce("statswrite", "Hunt stats: couldn't write " .. tmp) return end
    f:write(table.concat(out, "\n"))
    f:close()
    local bak = StatsFile .. ".bak"
    os.remove(bak)
    local hadOld = fileExists(StatsFile)
    local okB, errB = true, nil
    if hadOld then okB, errB = os.rename(StatsFile, bak) end
    local okR, errR = false, errB
    if okB then okR, errR = os.rename(tmp, StatsFile) end
    if not okR then
        if hadOld and okB then os.rename(bak, StatsFile) end
        os.remove(tmp)
        unlock()
        logOnce("statsrename", "Hunt stats: couldn't save " .. StatsFile .. ": " .. tostring(errR))
        return
    end
    os.remove(bak)
    unlock()
    pendingStats, statsDirty, pendingSince = {}, false, nil
    logOnce("statssaved", "Hunt stats: saving to " .. StatsFile)
end

local mpPlayers = {}
local function mpNote(ps, mine)
    if not MP_TEST then return end
    local id = tostring(readNum(ps, "PlayerId") or addressOf(ps) or "?")
    local c = mpPlayers[id]
    if not c then
        c = {name = playerNameOf(ps) or "unreadable", counted = 0, skipped = 0}
        mpPlayers[id] = c
        log(string.format("MP test: new player in hunt: %s (id %s), counted as you: %s", c.name, id, mine and "yes" or "no"))
    end
    if mine then c.counted = c.counted + 1 else c.skipped = c.skipped + 1 end
end

local function mpSummary()
    if not MP_TEST then return end
    local n = 0
    for id, c in pairs(mpPlayers) do
        n = n + 1
        log(string.format("MP test: hunt ended: %s (id %s): %d events counted, %d skipped", c.name, id, c.counted, c.skipped))
    end
    log("MP test: players seen in this hunt: " .. n)
end

local function addStatFor(ps, key, amount, saveNow)
    local mine = isMe(ps)
    mpNote(ps, mine)
    if not mine then return end
    local w = weaponKeyOf(ps)
    local p = pendingStats[w]
    if not p then p = {} pendingStats[w] = p end
    if STAT_IS_MAX[key] then
        if amount > (p[key] or 0) then p[key] = amount end
    else
        p[key] = (p[key] or 0) + amount
    end
    statsDirty = true
    if not pendingSince then pendingSince = os.time() end
    flushStats(saveNow)
end

local function addStat(obj, key, amount, saveNow)
    local ps = statsPlayerState(obj)
    if not ps then logOnce("statsnops:" .. key, "Hunt stats: couldn't tell which player a " .. key .. " event belonged to") return end
    addStatFor(ps, key, amount, saveNow)
end

local splitOwner = {}
local lastHitterOf = {}
local function onDamageSplit(self, part, core, stagger, wound, tags, behemoth)
    local obj = self:get()
    local ps = statsPlayerState(obj)
    if not ps then logOnce("statsnops:split", "Hunt stats: couldn't tell which player some damage belonged to") return end
    local okB, b = pcall(function() return behemoth:get() end)
    local key = tostring(addressOf(ps) or 0) .. ":" .. tostring((okB and addressOf(b)) or 0)
    local me = addressOf(obj) or 0
    local owner = splitOwner[key]
    if owner == nil or (owner.addr ~= me and not valid(owner.obj)) then
        owner = {addr = me, obj = obj}
        splitOwner[key] = owner
    end
    if owner.addr ~= me then return end
    if okB and valid(b) then lastHitterOf[addressOf(b) or 0] = ps end
    local function amount(p) local ok, v = pcall(function() return p:get() end) return (ok and type(v) == "number" and v > 0) and v or 0 end
    local c, p, s, w = amount(core), amount(part), amount(stagger), amount(wound)
    if c > 0 then addStatFor(ps, "core_damage", c) end
    if p > 0 then addStatFor(ps, "part_damage", p) end
    if s > 0 then addStatFor(ps, "stagger_damage", s) end
    if w > 0 then addStatFor(ps, "wound_damage", w) end
end

local FAUNA_HOOK = "/Game/_shared/blueprints/components/creature_telemetry_bpc.creature_telemetry_bpc_C:POSTCreatureKilledEvent"
local faunaHooked, faunaTryAt = false, -100
local function onFaunaKilled(self, guid, killer)
    local okK, k = pcall(function() return killer:get() end)
    if not okK or not valid(k) then return end
    local ps = statsPlayerState(k)
    if ps then addStatFor(ps, "fauna_kills", 1) end
end
local function hookFauna(force)
    if faunaHooked or not HUNT_STATS then return faunaHooked end
    local now = os.time()
    if not force and now - faunaTryAt < 3 then return false end
    faunaTryAt = now
    local ok = pcall(function()
        RegisterHook(FAUNA_HOOK, function(...)
            if isPlayedCopy() then return end
            local okE, e = pcall(onFaunaKilled, ...)
            if not okE then logOnce("statserr:fauna", "Hunt stats: something went wrong counting a fauna kill: " .. tostring(e)) end
        end)
    end)
    if ok then
        faunaHooked = true
        log("Hunt stats: fauna kills hooked")
    end
    return faunaHooked
end

local GATHER_HOOK = "/Game/Blueprints/Gatherables/gatherable_item_base_bp.gatherable_item_base_bp_C:GiveGatherableRewards"
local GATHER_KINDS = {
    gatherable_common_01_C = "gatherables",
    gatherable_special_01_C = "gatherables",
    gatherable_pylon_01_C = "gatherables",
    gatherable_defense_01_C = "gatherables",
    gatherable_healing_01_C = "gatherables",
    gatherable_damage_01_C = "gatherables",
    gatherable_speed_01_C = "gatherables",
    gatherable_weapon_01_C = "gatherables",
    gatherable_patrol_chest_01_C = "patrol_chests",
}
local gatherHooked, gatherTryAt = false, -100
local function onGatherRewards(self, pc)
    local okS, g = pcall(function() return self:get() end)
    if not okS or not valid(g) then return end
    local okN, cls = pcall(function() return g:GetClass():GetFName():ToString() end)
    if not okN then return end
    local key = GATHER_KINDS[cls]
    if not key then return end
    local okP, p = pcall(function() return pc:get() end)
    if not okP or not valid(p) then return end
    local ps = statsPlayerState(p)
    if ps then addStatFor(ps, key, 1) end
end
local function hookGather(force)
    if gatherHooked or not HUNT_STATS then return gatherHooked end
    local now = os.time()
    if not force and now - gatherTryAt < 3 then return false end
    gatherTryAt = now
    local ok = pcall(function()
        RegisterHook(GATHER_HOOK, function(...)
            if isPlayedCopy() then return end
            local okE, e = pcall(onGatherRewards, ...)
            if not okE then logOnce("statserr:gather", "Hunt stats: something went wrong counting a pickup: " .. tostring(e)) end
        end)
    end)
    if ok then
        gatherHooked = true
        log("Hunt stats: pickups hooked")
    end
    return gatherHooked
end

local PROC_ELEMENTS = {"blaze", "frost", "shock", "radiant", "umbral", "terra"}
local elemZeroAt = {}
local function onElementalHealth(self, element, maxHealth, current)
    local okB, b = pcall(function() return self:get() end)
    if not okB or not valid(b) then return end
    local okE, el = pcall(function() return element:get() end)
    local okC, cur = pcall(function() return current:get() end)
    if not okE or not okC or type(el) ~= "number" or type(cur) ~= "number" then return end
    local addr = addressOf(b) or 0
    if cur <= 0 then elemZeroAt[addr] = os.clock() return end
    if cur <= 1 then return end
    local z = elemZeroAt[addr]
    if z and os.clock() - z < 1 then return end
    local ps = lastHitterOf[addr]
    if not ps or not valid(ps) then logOnce("statsnops:procs", "Hunt stats: couldn't tell who caused an elemental status") return end
    addStatFor(ps, "procs", 1)
    local name = PROC_ELEMENTS[el]
    if name then addStatFor(ps, "procs_" .. name, 1) else logOnce("procel:" .. tostring(el), "Hunt stats: unknown status element " .. tostring(el)) end
end

local STAT_HOOKS = {
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordPlayerDamageExecution", function(self, exec)
        hookFauna()
        hookGather()
        local total = exec:get().Total
        if type(total) == "number" and total > 0 then
            addStat(self:get(), "biggest_hit", total)
        end
    end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordBehemothStaggered", function(self, behemoth, wasInterrupt)
        local okI, interrupt = pcall(function() return wasInterrupt:get() end)
        addStat(self:get(), (okI and interrupt == true) and "interrupts" or "staggers", 1, true)
    end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordBehemothPartBroken", function(self) addStat(self:get(), "parts_broken", 1, true) end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordBehemothPartExposed", function(self) addStat(self:get(), "wounds", 1) end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordEnterBleedOut", function(self) addStat(self:get(), "deaths", 1, true) end},
    {"/Script/Archon.BountyComponent:ServerClaimBounty", function(self) addStat(self:get(), "bounties", 1, true) end},
    {"/Script/Archon.MasteryComponent:OnBehemothKilled", function(self, behemoth)
        local obj = self:get()
        addStat(obj, "kills", 1)
        local ps = statsPlayerState(obj)
        local okB, b = pcall(function() return behemoth:get() end)
        local full = okB and valid(b) and nameOf(b)
        local id = full and idOf(full)
        local deadAddr = okB and valid(b) and addressOf(b)
        if deadAddr then lastHitterOf[deadAddr], elemZeroAt[deadAddr] = nil, nil end
        if ps and isMe(ps) and id and id ~= "" and not id:find("_clone", 1, true) and not id:find("^dummy") then
            local p = pendingStats[KILLS_SECTION]
            if not p then p = {} pendingStats[KILLS_SECTION] = p end
            p["kill_" .. id] = (p["kill_" .. id] or 0) + 1
            local wk = weaponKeyOf(ps)
            local wp = pendingStats[wk]
            if not wp then wp = {} pendingStats[wk] = wp end
            wp["wkill_" .. id] = (wp["wkill_" .. id] or 0) + 1
            statsDirty = true
            if not pendingSince then pendingSince = os.time() end
        end
        flushStats(true)
    end},
    {"/Script/Archon.Objective_Hunt:OnDamagedBehemoth", onDamageSplit},
    {"/Script/Archon.ArchonInteractionUserComponent:ServerStartInteraction", function() hookGather(true) end},
    {"/Script/Archon.PlayerExperienceComponent:HandleCreatureKilled", function(self)
        if faunaHooked then return end
        if hookFauna(true) then return end
        local ps = statsPlayerState(self:get())
        if ps then addStatFor(ps, "fauna_kills", 1) end
    end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordHealReceived", function(self, source, sourceObject, amount)
        local okA, a = pcall(function() return amount:get() end)
        if not okA or type(a) ~= "number" or a <= 0 then return end
        local okO, o = pcall(function() return sourceObject:get() end)
        local okN, cls = pcall(function() return o:GetClass():GetFName():ToString() end)
        if okO and okN and type(cls) == "string" and cls:lower():find("revive", 1, true) then return end
        addStat(self:get(), "heal_received", a)
    end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordDamageReceived", function(self, source, damage)
        local okD, d = pcall(function() return damage:get() end)
        if okD and type(d) == "number" and d > 0 then addStat(self:get(), "damage_taken", d) end
    end},
    {"/Script/Archon.ArchonBehemoth:SetElementalHealth", onElementalHealth},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordPerfectDodge", function(self) addStat(self:get(), "perfect_dodges", 1) end},
    {"/Script/Archon.ArchonTelemetryComponent_Combat:RecordPlayerMatchResult", function() mpSummary() flushStats(true) end},
}

local function hookStats()
    if not HUNT_STATS then return end
    local okCount, failed = 0, {}
    for _, h in ipairs(STAT_HOOKS) do
        local path, fn = h[1], h[2]
        local ok, err = pcall(function()
            RegisterHook(path, function(...)
                if isPlayedCopy() then return end
                local okE, e = pcall(fn, ...)
                if not okE then logOnce("statserr:" .. path, "Hunt stats: something went wrong in " .. path .. ": " .. tostring(e)) end
            end)
        end)
        if ok then okCount = okCount + 1 else failed[#failed + 1] = path .. " (" .. tostring(err) .. ")" end
    end
    log(string.format("Hunt stats are on (%d of %d events hooked)%s", okCount, #STAT_HOOKS,
        #failed > 0 and ("; could not hook: " .. table.concat(failed, ", ")) or ""))
    hookFauna()
    hookGather()
end

local townPC = nil
local ClearFile = DataDir and (DataDir .. "\\healthbar-clear.txt")
local pendingClear = false
local function clearRequested()
    if not ClearFile then return false end
    local f = io.open(ClearFile, "r")
    if not f then return false end
    f:close()
    os.remove(ClearFile)
    return true
end

local function tick()
    local now = os.clock()
    local pc = getPC(now)
    if not pc then
        if next(entries) ~= nil then
            local n = 0
            for k in pairs(entries) do drop(k) n = n + 1 end
            log(string.format("No local player found; cleared %d bar(s)", n))
            writeBars(now)
        end
        lastKnown = {}
        return
    end
    local okC, asked = pcall(clearRequested)
    if okC and asked then pendingClear = true end
    startOverlay()
    notePlayerName(pc, now)
    if now < settleUntil then return end

    local where = (nameOf(pc) or ""):lower()
    if where:find("/maps/ramsgate/", 1, true) or where:find("map_loginmenu", 1, true) then
        if pc ~= townPC then
            townPC = pc
            log("In Ramsgate or Main Menu; pausing behemoth searches")
        end
        if next(entries) ~= nil then
            local n = 0
            for k in pairs(entries) do drop(k) n = n + 1 end
            log(string.format("Back in Ramsgate or Main Menu, cleared %d bar(s)", n))
        end
        hitQueue, tracked, lastKnown = {}, {}, {}
        pendingClear = false
        writeBars(now)
        return
    end
    townPC = nil

    if pendingClear then
        pendingClear = false
        local all = {}
        for k in pairs(entries) do all[#all + 1] = k end
        for _, k in ipairs(all) do drop(k) end
        hitQueue = {}
        for _, t in pairs(tracked) do t.hitAt = nil end
        log(string.format("Clear key pressed, cleared %d bar(s)", #all))
    end

    local seen = {}
    local q = hitQueue
    hitQueue = {}
    local done = {}
    for _, comp in ipairs(q) do
        local cn = nameOf(comp)
        if cn and not done[cn] then
            done[cn] = true
            local b = hitBehemoth(comp)
            local key = b and nameOf(b)
            if key then
                if not tracked[key] then tracked[key] = {obj = b} end
                tracked[key].hitAt = now
            end
        end
    end
    for key, t in pairs(tracked) do
        local b = t.obj
        if valid(b) then
            local a = attrFor(key, b)
            if not a then logOnce("noattr:" .. key, "No health info yet for " .. idOf(key)) end
            local hp = a and readNum(a, "CurrentHealth")
            local mx = a and readNum(a, "MaxHealth")
            if hp and mx and mx > 0 then
                seen[key] = true
                t.missAt = nil
                local s = readNum(a, "CurrentShield") or 0
                if not entries[key] and t.hitAt and hp > 0 then
                    t.hitAt = nil
                    engage(key, hp, mx)
                end
                updateEntry(key, b, hp, mx, s, now)
            elseif entries[key] then
                t.missAt = t.missAt or now
                if now - t.missAt < READ_GRACE then seen[key] = true end
            end
        end
    end
    local gone = {}
    for k, e in pairs(entries) do
        local idle = (not e.deadAt) and (now - (e.changedAt or now) > IDLE_DROP)
        local why
        if not seen[k] then
            why = string.format("not seen (it is gone, or its health could not be read for %.1f s; health was %.0f / %.0f)", READ_GRACE, e.hp, e.mx)
        elseif e.deadAt and now - e.deadAt > math.max(LINGER, 0.5) then
            why = "dead"
        elseif idle then
            why = string.format("no damage for %.0f s", now - e.changedAt)
        end
        if why then
            log(string.format("Removed the bar for %s (%s)", tostring(e.id), why))
            gone[#gone + 1] = k
        end
    end
    for _, k in ipairs(gone) do drop(k) end
    for k, t in pairs(tracked) do
        if not entries[k] and not (t.hitAt and now - t.hitAt < 10) then
            tracked[k] = nil
            attrSets[k], attrMissAt[k], staggers[k], staggerMissAt[k], noStagger[k] = nil, nil, nil, nil, nil
        end
    end

    writeBars(now)
end

local function run()
    local ok, e = pcall(tick)
    if not ok then logOnce("tick", "Something went wrong during an update: " .. tostring(e)) end
end

local nextTickAt = 0
local function onCharacterTick()
    local now = os.clock()
    if now < nextTickAt then return end
    nextTickAt = now + TICK_EVERY
    run()
end

local tickHooked, tickTries = false, 0

local function hookTick(why)
    if tickHooked then return end
    tickTries = tickTries + 1
    local ok, err = pcall(function() RegisterHook(TICK_HOOK, onCharacterTick) end)
    if not ok then
        local ok2, path = pcall(function()
            local o = FindFirstOf(TICK_CLASS)
            if o and o:IsValid() then
                local full = o:GetClass():GetFullName()
                return full and full:match("^%S+%s+(.+)$")
            end
        end)
        if ok2 and path then
            ok, err = pcall(function() RegisterHook(path .. ":ReceiveTick", onCharacterTick) end)
        end
    end
    if ok then
        tickHooked = true
        log("Bar updates are running off the character tick (" .. why .. ")")
    elseif tickTries <= 3 then
        log("Couldn't hook the character tick yet (" .. why .. "): " .. tostring(err) .. ", attempting again on the next loading screen")
    end
end

local okR, errR = pcall(function()
    RegisterHook(RESTART_HOOK, function()
        if not isPlayedCopy() then return end
        local ok, e = pcall(hookTick, "after loading")
        if not ok then logOnce("restart", "Something went wrong hooking the character tick: " .. tostring(e)) end
    end)
end)
if not okR then log("Loading screen detection unavailable. Health bars disabled: " .. tostring(errR)) end

local okH, errH = pcall(function()
    RegisterHook(HIT_HOOK, function(self, Entries)
        if not isPlayedCopy() then return end
        if not tickHooked and tickTries < 5 then pcall(hookTick, "on a hit") end
        local ok, e = pcall(function() onHits(Entries:get(), self:get()) end)
        if not ok then logOnce("hit", "Couldn't read a hit: " .. tostring(e)) end
    end)
end)
if not okH then log("Couldn't monitor hit events. Health bar display disabled: " .. tostring(errH)) end

local okSt, errSt = pcall(hookStats)
if not okSt then log("Hunt stats couldn't start: " .. tostring(errSt)) end

log("v2.3.0 loaded, bars go to " .. tostring(OutFile)
    .. (AUTO_START_OVERLAY and "  (the mod starts the overlay)" or "  (auto_start_overlay = false: start the overlay yourself)")
    .. (okH and "" or ("  (hit hook failed: " .. tostring(errH) .. ")")))