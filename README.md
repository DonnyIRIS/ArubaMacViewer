# Aruba Switch MAC Address Viewer

Windows desktop app (C# / .NET 8 WinForms) that reads a list of HP/Aruba switches from a
text file, logs into each one over SSH, runs `show mac-address`, and shows every connected
device in a checkable/selectable list you can filter, sort, copy, or export to CSV.

Supports:
- **ArubaOS-Switch / ProVision** – 2530, 2540, 2930 etc. (`show mac-address`)
- **AOS-CX** – 6000, 6100, 6200, 6300 etc. (falls back to `show mac-address-table`)

## Requirements
- Visual Studio 2022 with the ".NET desktop development" workload (or the .NET 8 SDK)
- NuGet package **SSH.NET** (restored automatically on first build)
- SSH enabled on the switches (`ip ssh` on ProVision, `ssh server vrf mgmt` on AOS-CX)

## Editing the window
`MainForm.cs` is a normal designer-backed form: in Solution Explorer double-click **MainForm.cs**
(or right-click → View Designer) to open the drag-and-drop Forms Designer. The layout lives in
`MainForm.Designer.cs`; the logic and event handlers live in `MainForm.cs`.

## Build & run
1. Open `ArubaMacViewer.sln` in Visual Studio.
2. Press **F5** (NuGet restore happens automatically).

## Switch list: Excel or text
**Excel (.xlsx)** - a sheet (preferably named "Devices") with a header row containing at least an
*IP Address* column; *Country*, *Company*, *City* and *Type* are used when present:
```
Country | Company | City   | Type     | IP Address
CA      | Menard  | Ottawa | Firewall | 10.162.160.254
CA      | Menard  | Ottawa | Switch   | 10.162.160.10
```
Rows with Type *Switch* become the switch list; the **Site** column shows `Company - City (Country)`.
Rows with Type *Firewall* are used as the known gateways of their subnets (read for DHCP leases /
ARP, no .1/.254 probing needed) and give the site names in the Excel-by-site report. A *Site* or
*Switch Name* column is used directly if present. Excel lists are never rewritten automatically;
**Save list with names** writes a new workbook (Site, Type, IP, Switch Name, Model).

**Aruba AP login** (Settings tab): the credentials of your access points. They are tried *last*,
only after the switch logins were rejected; a device that accepts them is confirmed with the AP CLI
(`show version` -> `ArubaOS (MODEL: 505)`, `show summary`) and removed from the list.
Access points are removed from the list automatically: rows whose Type is *AP*, *Access Point*,
*Wireless* or *WiFi* are left out on load; a device that logs in and turns out to run ArubaOS
(`ArubaOS (MODEL: 505)`) disappears from the list; and any list IP that a queried switch reports as
an access point over LLDP is removed too. If your addressing plan reserves a host number for APs (e.g. every `x.x.x.3`), put it in
**Skip list IPs ending in** on the Settings tab (default `3`; several allowed, `3,4`) and those
addresses are left out on load. Right-click a row for **Remove - not a switch** to do it by hand. Removed devices are remembered so they stay out on the next load; **Forget ignored
devices (APs)** on the Settings tab clears that memory.

**Text (.txt)** - one switch per line, `IP`, `IP,Name` or `IP,Name,Site`. Lines starting with `#` are ignored.
```
192.168.1.10
192.168.1.20,Access-6100-48G
```
Names are read from the switch itself (`show system`) when you connect. After each run the app
rewrites the loaded file as `IP,Name` (on by default - *Update the switch list file...* on the
Settings tab), and **Save list with names** on the Switches tab writes it wherever you like, so
the next time you open the program the names are already there. Names are also cached in
`%LocalAppData%\ArubaMacViewer\switch-names.json`.

The switch list starts empty each time the app opens; click **Browse...** to pick your file (a sample `switches.txt` is included in the project folder).

## Step-by-step guide
A green strip under the top bar tells you what to do next and the button (or tab, or field) to use
is highlighted green: Browse -> credentials -> Get MAC table (ALL) -> Connected devices tab ->
Bulk select -> Change access VLAN -> (optional) Export Excel. It advances automatically as each
step is completed. Untick **Show guide** to hide it.

## Using the app
1. Enter the switch username/password (e.g. `manager`). Tick **Remember credentials** to save them
   for next time – they are encrypted with Windows DPAPI (current-user scope) in
   `%LocalAppData%\ArubaMacViewer\credentials.dat`, so only your Windows account on this PC can
   decrypt them. Untick the box to delete the saved file.
2. **Switches tab** – every switch is a row with a checkbox, name, IP, status and last-updated time.
   Tick the switches you want and click **Get MAC table (checked)**, double-click a single row,
   or use **Get MAC table (ALL)**. The Status column shows Querying / OK - n devices / Error.
   **Get name from switch (checked)** only logs in and reads the hostname (`show system`) for the
   checked switches (or all of them if none are checked) without pulling the MAC table – handy for
   naming a freshly loaded IP list.
3. **Connected devices tab** – the **Vendor** column identifies the device maker from the MAC's OUI
   (IEEE registry). Type a vendor name in the filter box (e.g. `polycom`) to see only those devices.
   Randomized/locally-administered MACs are labelled as such.
   **Device type** goes further than the vendor: the app reads each switch's LLDP/CDP neighbour table
   (`show lldp info remote-device` + `show cdp neighbors detail` on ProVision, `show lldp neighbor-info
   detail` on AOS-CX). A device that announces itself is identified precisely - "Wireless access
   point" for an Aruba AP (`ArubaOS (MODEL: 515)`), "IP phone", "Switch", "Firewall"... - and the
   **LLDP/CDP neighbor** column shows its name, description and management IP. Devices that don't
   speak LLDP/CDP are classified from the vendor (PC / laptop, IP camera, Printer, UPS, VM...).
   The app also reads, per device: **IP address** (DHCP-snooping binding, AOS-CX client tracker or
   ARP), **DNS name** (reverse lookup from your PC), **802.1X / MAC-auth** result (user, method,
   role), and per port: **Port name** (interface name/description), **PoE** (status, watts, class)
   and **Link** (up/down, speed). **Double-click any row** for a details window listing everything
   known about that device, including the full LLDP/CDP record and the other devices on the port.
   All of this can be turned off on the Settings tab (it adds several commands per switch).
   **Gateway auto-detect** (on by default): for each switch subnet the app finds the local firewall
   without guessing, trying in this order: the firewall listed for the subnet in the Excel (or the
   one that answered last time); the **default gateway configured on the switches** in that subnet
   (`ip default-gateway` / `ip route 0.0.0.0/0 ...`, or - when the address came from DHCP - the live `show ip route [vrf mgmt]` / `show ip`); the IP of any **Palo Alto Networks MAC learned
   in the firewall mgmt VLAN** (Settings, default 144) - so `.253` in LatAm or any other host number is
   found; and finally the *Gateway host numbers* (default `1,254,253`) over SSH. If one answers and is a Palo Alto, it reads `show dhcp server lease
   interface all` (IP, DHCP hostname, VLAN sub-interface) and `show arp all`; any other router/core
   switch gets the ARP/DHCP-snooping commands instead. The DHCP hostname appears in the DNS name
   column as `name (DHCP)` when there is no PTR record. Firewalls are tried with the main login first
   (e.g. the AD account); if that is rejected the **Firewall backup login 1** and **2** from the
   Settings tab are tried, then the switch alternate. The set that worked is remembered per gateway.
   All are saved encrypted with the other credentials. Discovered gateways are also polled by the IP watch, so a lease
   showing up on the new VLAN's sub-interface is what verifies a move.
   Access switches without DHCP snooping only know the IPs on their own management subnet, so on the
   Settings tab you can also list explicit **ARP sources** - the core switch, router or Palo Alto gateways. After
   each MAC-table run the app logs into those (same credentials, ARP table only; ProVision, AOS-CX,
   Cisco and PAN-OS command forms are all tried) and fills in the IP for every MAC it finds.
   **Port type** (from the running config) is **Access** when the port only has an untagged VLAN and
   **Trunk** when it carries any tagged VLAN (LAGs are marked "Trunk (LAG)" / "LAG member").
   **Port VLANs** gives the detail: `Tagged 128, untagged 112`, `Access (VLAN 112)`,
   `LAG Trk1 (25,26) · Tagged 1,128`, or on AOS-CX `Trunk (native 10, allowed 10,128)`.
   A switch keeps its MAC table per VLAN, so a device on a trunk port appears once per VLAN it uses.
   **One row per MAC/port** (on by default) merges those into a single row whose VLAN column lists
   all VLANs, e.g. `112, 116, 128, 144`; untick it to see the raw per-VLAN entries.
   Devices from phone / camera / printer / IoT vendors (Yealink, Polycom, Axis, Xerox, APC, Sonos...)
   are shown with an **orange** background, also in the Excel report; edit the list under
   **Highlight vendors** on the Settings tab.
   Trunk rows are shown in dark blue; type "Trunk" or "Access" in the filter box to isolate them.
   The **Note** column labels ports using the **Port labels** rules on the Settings tab, one per
   line as `vlan[vendor]=label`. Default `144[Palo Alto]=MGT PORT` marks ports whose access VLAN is
   144 *and* whose device is a Palo Alto Networks unit. Omit the vendor for any device
   (`128=VOICE`) or use `*[Polycom]=PHONE` to match a vendor on any VLAN. First matching rule wins.
   **Connected devices tab (cont.)** – opens automatically when a query finishes. Devices are grouped
   under a header per switch (`Name  (IP)` with a device count); click anywhere on a header to
   collapse/expand that switch, or use **Expand all** / **Collapse all**. Untick **Group by switch**
   for one flat list. Click column headers to sort (within groups), type in the filter box to narrow
   the list, tick the checkboxes (or **Check all**) and use **Copy checked** / **Export checked to Excel...** (one sheet, every column).

## Firmware upgrade (tab)
Upgrade switch OS across the fleet and watch each switch copy, reboot and come back.

1. **Quickest: pick a model, then browse.** The top row of the tab has a **Model** dropdown (every model in
   the switch list with its switch count and current image; you can also type one, e.g. `2930F`) and
   **Browse image for this model...**. Choose the `.swi` and it is assigned to that model straight away;
   if the file name says it's for a different platform you're asked to confirm. Browsing again replaces
   the model's image; picking a file that's already loaded adds the model to that image.
   **Or: Add image(s)** - one or more `.swi` files. Each image is **associated with specific switch models**:
   when you add it, a picker lists every model in the loaded switch list (with how many switches have
   it and which image it already uses) and pre-ticks the models the file name fits
   (`YA_` -> 2530, `WB_` -> 2920, `WC_` -> 2930F/2930M, `KB_` -> 3810M/5406R/5412R,
   `ArubaOS-CX_6000_6100_...` -> 6000/6100, `ArubaOS-CX_6200_...` -> 6200F). Confirm or change the ticks,
   or type other models. A model uses exactly one image (ticking a model moves it from another image).
   **Assign models...** (or double-click an image) changes it later. Run *Get name and model* or
   *Check versions* first so every model appears. As a safety net a ProVision image is still refused on
   a switch whose running software family differs (e.g. a WC image on a YA 2530).
2. **Troubleshooting the built-in TFTP server.** The switch has to open a connection *to this PC* (UDP 69,
   then UDP between ephemeral ports). The app now picks the PC address Windows uses to reach the switches,
   offers **Allow TFTP in Windows Firewall (admin)**, and gives up after 90 s if the switch never asks for the
   file (log: "the switch never requested the image ..."), or if a transfer stalls for 2 min, showing what the
   switch printed. If that happens, the usual cause is a Palo Alto between the site and this PC not allowing
   the `tftp` application from the switch to this PC, a Group-Policy-managed Windows Firewall, or this PC being
   reached through Zscaler/VPN NAT - then use an external TFTP server inside the network.
   **Address the built-in server offers:** an address you pick in the dropdown is always kept. Otherwise the
   app uses the address on the route to the switches - but never a Zscaler/VPN/tunnel address (100.64.0.0/10,
   169.254/16, or an adapter named Zscaler/VPN/Tunnel/GlobalProtect/AnyConnect), because switches can't
   connect back to those. **External TFTP server** (e.g. SolarWinds) is unaffected by any of this.
3. **Image source** - three choices:
   - **Push from this PC over SSH (recommended, v1.2.7).** The app sends the image itself over TCP, so the
     copy runs at line speed - a 15 MB 2530 image in well under a minute instead of 10-40 minutes - and the
     Progress column shows the real byte count and MB/s. It uses the same login as the shell session and the
     same path (port 22 through Zscaler/ZPA included), so there is no TFTP server, no UDP 69 firewall rule and
     no "which PC address can the switch reach" question.
     *ProVision (2530/2920/2930F):* SFTP into `/os/secondary` (or `/os/primary`) on the switch's own SSH server.
     If `Secure Copy Enabled` is `No` in `show ip ssh`, the app runs `ip ssh filetransfer` first (that
     disables TFTP on the switch - no longer needed - and is saved with the config). The switch validates and
     writes the image after the file is closed (a minute or more on a 2530); the app polls `show flash` until the
     slot shows the new version, so the Progress column sits at "Switch validating and writing" for that time.
     *AOS-CX (6000/6100/6200F):* REST API upload over HTTPS (`PUT /rest/v10.04/firmware?image=secondary`)
     with the same login. The app checks `show https-server` and enables
     `https-server rest access-mode read-write` / `https-server vrf <vrf>` if needed; TCP 443 from this PC to
     the switch must be allowed (firewall, ZPA application segment). The switch writes flash after the upload;
     the app polls `show images` until the slot shows the new version.
     If a push fails, the log has the switch's reason; pick a TFTP source for that switch as the fallback.
   - **Built-in TFTP server** (switches download straight from this PC, which also gives a copy %). Pick the
     PC IP the switches can reach; if a switch can't connect, allow UDP 69 through Windows Firewall (the app
     logs the exact `netsh` command).
   - **External TFTP server** `server[/dir]` (e.g. SolarWinds). Slowest: TFTP sends 512 bytes per round trip,
     so a 15 MB image at 50 ms RTT takes about 25 minutes, and no option on either side changes that.
3. **Options** - flash slot (secondary by default), save config, set default boot, reboot after copy,
   wait-and-verify, reboot-wait minutes, and how many switches at once (default 2). AOS-CX VRF is auto
   (mgmt if the switch has a mgmt VRF, else default) or forced.
4. **Check versions** reads each switch's running/primary/secondary version and marks it Ready, Already
   current, Running newer, or No image. **Start upgrade (checked)** then, per switch:
   copy to the slot -> verify the slot shows the new version -> save config (`write memory`) -> promote ->
   set default boot -> read the uptime -> reboot -> watch the session drop and wait for the switch to come
   back -> log in and confirm the running version.
   **Promote (option *Copy secondary -> primary, boot primary*, on by default).** On **AOS-CX** this runs
   `copy secondary primary` (which does not reboot), so both slots hold the new OS and the switch boots
   primary. On **ProVision** there is no in-place flash-to-flash copy: `copy flash flash primary` *reboots the
   switch immediately* (that is how it applies the copy), so the app does **not** use it - it boots the new
   image straight from **secondary** instead. The switch comes up on the new OS either way; primary is left on
   the old image as an automatic fallback, and the next upgrade pushes to secondary again. Untick the option to
   boot secondary explicitly (same result on ProVision).
   **How the app proves the reboot (v1.2.6).** It does *not* trust TCP port 22: on a PC that reaches the
   switches through Zscaler ZPA the handshake is completed locally, so the port looks open while the switch is
   down (and looked "open" for 3 minutes in v1.2.5). Instead the app stays on the SSH session after the boot
   command, answers every y/n question the switch asks (the reboot confirmation, and *Do you want to save
   current configuration* if the config is dirty), logs everything the switch prints, and treats a closed
   session as the switch going down. If the session is only quiet, it sends an Enter: a prompt coming back
   means the switch did not reboot (the job fails right there); silence means it is going down. The switch is
   then polled with real SSH logins (20 s each). Once a login works, the uptime read before the boot command
   (`show uptime`, fallback *Up Time* in `show system`) is compared with the uptime now: a short uptime proves
   the reboot and the running version is checked; a long one means the switch never rebooted and the job
   fails saying so, with the reboot output in the log.
   During the copy the Progress column shows the switch's own output line and the elapsed time
   (e.g. `Copying 3:12 - TFTP download in progress`; with the built-in server also a percentage), and the log
   gets a line every minute. Only the built-in server can see the download itself; with an external server
   (SolarWinds) the app relies on the switch: when the switch returns to its prompt, `show flash` is read and
   the slot must show the new version before anything else happens.
   The grid is grouped and sorted by model - each group header shows the model, how many switches,
   the assigned image and target version, and how many are done - and shows Switch, Model, Current,
   Target, Stage, Progress, Elapsed and Result live, with an overall progress bar; colours mark done / already-current / staged / failed. **Cancel** stops
   starting new switches (any already rebooting finish on their own).

Switches already on the target version are skipped. A switch-specific login (right-click a switch >
Set login for this switch) is used here too.

**Renamed image files are fine.** The version is found anywhere in the file name, with `_ . -` or spaces
between the parts and extra text around it (`YA_16_11_0032-2530.swi`, `2530 YA.16.11.0032 core.swi`,
`WC-16-11-0019_2930F.swi`, `ArubaOS-CX_6000_6100_10_13_1000-site.swi`, `PL_10_13_1000.swi`); other model
numbers in the name (2530, 2930F, 6200F) pre-tick those models. If the name has no version at all, the app reads the version
embedded inside the image (e.g. `WC.16.11.0019`, `PL.10.13.1000`) and asks you to confirm it; if none is
found you type it. The images list shows "(from file contents)" / "(from entered)" next to such versions.
The built-in TFTP server offers the file under a name the switch CLI accepts (spaces and special
characters become `_`); with an external TFTP server, keep names to letters, digits, `.`, `_` and `-`.
For AOS-CX images whose version letters are known (PL, ML, FL ...), the upgrade also refuses a switch
running different letters.

The **Switches** tab is sorted and grouped by model as well (2530, 2930F, 6000, 6200F ...; switches
whose model hasn't been read yet are listed last).

## Everything you enter is remembered
- **Settings tab** values: `%LocalAppData%\ArubaMacViewer\settings.json` (as before).
- **Passwords** (LDAP, alternate, firewall, Aruba AP, switch-specific): encrypted with Windows DPAPI in
  `credentials.dat` / `switch-credentials.dat` when *Remember credentials* is ticked.
- **Everything else**: `%LocalAppData%\ArubaMacViewer\ui-state.json`, saved a second after each change and
  again on close. That covers:
  - the switch list file, which is re-opened automatically at start-up;
  - window size, position and maximised state;
  - the last open tab;
  - the Connected devices filter, MAC search, Ports, port scope, *Group by switch* and *One row per MAC/port*;
  - the Firmware upgrade tab: images and their model assignments, the TFTP source (built-in address you
    picked, or external server), slot, VRF, all option checkboxes, reboot wait, switches at once, and the
    Model box;
  - the Bulk select dialog's MAC list, vendor, VLAN and checkboxes.
- Remembered images whose `.swi` file has been moved or deleted are skipped, with a note in the log.

## Choosing which switches to upgrade (tick boxes on the Firmware tab)
The Firmware upgrade grid has a tick box per switch (v1.3.7). Both buttons work on the **ticked rows only**
when any are ticked: **Check versions** reads those switches and leaves ticked the ones that need the image
(already-current ones are unticked); **Start upgrade (checked)** runs exactly the ticked rows and unticks them
when they finish. With nothing ticked, Check versions reads every switch in the grid (and ticks the ready
ones); Start upgrade does nothing until something is ticked. *Check all*, *Uncheck all* and *Check ready*
(in the button row next to Retry later) adjust the ticks by hand; a failed row can be ticked again to retry. Within each
model group the ticked switches are listed first, then the ones still to do (ready, failed, not yet checked),
then the finished and already-current ones. The grid shows all switches in the list, or only those checked on
the Switches tab if any are.

## Firmware report (Export to Excel)
**Export to Excel...** on the Firmware upgrade tab (top right, next to Options) writes the switches shown in the
grid to an .xlsx: switch, IP, site, model, **previous version**, **current version**, status (Upgraded /
Upgraded (earlier run) / Already current / Failed / Not upgraded yet / No image ...), when it was upgraded,
when the version was last read, and the last result - colour-coded, with a filter on every column and a
one-line summary underneath. Versions are also kept per switch in
`%LocalAppData%\ArubaMacViewer\upgrade-history.json`, so the previous version is still known after a later
*Check versions* or a restart (history starts with v1.3.6 - switches upgraded before that show their current
version only).

## Removing a switch from the list
Right-click a switch on the **Switches** tab (or select it and press **Del**), a device row on the
**Connected devices** tab, or a row on the **Firmware upgrade** tab and choose **Remove from list**. The switch
leaves all three tabs at once (its devices and its upgrade row go with it). The list file is rewritten without
it only when *auto-save switch names* is on in Settings - the same rule as for names; otherwise use *Save list
with names*. This is different from *Remove - not a switch*, which also remembers the device as an access point
so it is left out of every future load.

## Retry later (switches that failed)
Every switch the app could not log in to, or could not upgrade, is kept in a **Retry later** list
(`%LocalAppData%\ArubaMacViewer\retry-later.json`, so it survives a restart). What gets added:
a rejected login, a connection that never got as far as a login (no SSH banner, timeout, refused),
a failed *Test SSH*, a failed version check, and a failed upgrade - with the reason, the number of tries and
the time of the last one. A switch is taken off the list automatically as soon as it works later (a login
for login entries; an upgrade that finishes, or a version check that finds it already current, for upgrade
entries). The **Retry later (n)...** button on the Switches tab (and on the Firmware upgrade tab) opens the
list: tick the switches and either **Load checked as the switch list** (writes
`retry-later-switches.txt` next to the json and opens it, with each switch ticked) or **Add checked to the
current list**. Then run *Get MAC table (checked)* or *Check versions* / *Start upgrade* as usual. Entries can
be removed one by one or the whole list cleared.

## "Test SSH says OK but the connect fails: connection aborted by the server"
Test SSH only reads the banner line (`SSH-2.0-Mocana SSH 6.3` = old ProVision firmware). The real login then
negotiates key-exchange, cipher, MAC and host-key algorithms, and old Mocana firmware drops the socket when
nothing matches - the classic case is a switch whose only host key is **DSA** (`show ip ssh` -> Host Key Type:
DSA), which SSH.NET and modern OpenSSH no longer accept. The app retries once with a short legacy-friendly offer
and logs it ("legacy offer: ..."); if that is refused too, the error says what to do: on the switch,
`configure` / `crypto key generate ssh rsa bits 2048` / `write memory` (Host Key Type becomes RSA and the app
connects), or update the firmware.

## Login order
Every switch is tried in the same fixed order: **LDAP account** (the LDAP user/pass in the top bar) ->
**alternate login 1** -> **alternate login 2** (both in Settings > Authentication, either may be left
empty) -> the switch-specific login, if one was set for that switch ->
the Aruba AP login (used only to recognise access points). The order no longer changes based on what
worked last time, so the LDAP account is always tried first. Gateway firewalls keep their own order:
LDAP -> firewall backup 1 -> firewall backup 2 -> alternate, with the login that worked last time on that
firewall tried first.

## Switches with their own login
If a switch rejects every configured login (main, alternate, Aruba AP), then at the end of the run a
**Login rejected** dialog asks for that switch's username and password - *Retry now* re-queries just
those switches, *Skip* / *Skip all* move on. You can also right-click a switch any time and choose
**Set login for this switch...** (or *Clear switch-specific login*). A switch-specific login is tried
after the LDAP and alternate logins, for that switch only, and - with *Remember for this switch* ticked - saved encrypted with
Windows DPAPI in `%LocalAppData%\ArubaMacViewer\switch-credentials.dat`.

## Disable NAC on a port
Tick the devices on the Connected devices tab and click **Disable NAC on port (checked)...** (or right-click a
device row -> *Disable NAC on this port...*). The dialog lists one row per switch port, shows the commands it
will send (editable, remembered in settings.json; *Reset commands to default* restores them), previews the
exact CLI per switch, and runs them in configuration mode one switch at a time, with each switch's replies
shown and a per-port OK / error. The default commands, with `{port}` replaced by the port as the switch
names it (33, 1/33, A12):

    no aaa port-access authenticator {port}
    no aaa port-access mac-based {port}
    no spanning-tree {port} bpdu-protection
    no spanning-tree {port} auto-edge-port
    no spanning-tree {port} admin-edge-port

*Save configuration* (write memory) is on by default. These are ProVision commands, so rows on AOS-CX
switches, LAG ports and uplinks are listed but skipped. The changed switches are re-read afterwards so the
802.1X column reflects the new state.

## Access ports only (hide uplink / downlink / trunk sightings)
Every switch learns a device's MAC on the port that leads towards it, so one device shows up on its own
access port and again on the uplink/downlink of every switch in between. The **Access ports only** tick box
at the bottom of the Connected devices tab (on by default, remembered) hides the rows on switch links,
uplinks, trunks and LAGs so each device is listed once, on the port it is plugged into; the count line says
how many sightings are hidden. Untick it to see every sighting. (The *Ports* scope dropdown still offers
*Switch links only* when you want just those.)

## Copying from the grids
Right-click any cell on the Connected devices, Switches or Firmware upgrade grid: **Copy cell** (the menu shows
the value and column), **Copy row** (tab-separated, visible columns in their current order) and **Copy selected
rows (with headers)**. Keyboard: **Ctrl+C** copies the selected rows with a header line (every row when none
is selected), **Ctrl+Shift+C** copies the cell under the mouse, **Ctrl+A** selects all. Hidden columns are
left out. The *Copy* button on the Connected devices tab still copies the ticked devices with every column.

## Columns: show/hide, reorder, resize (remembered)
On the Connected devices, Switches and Firmware upgrade grids (v1.4.5): **drag a column header** to move the
column, **drag its edge** to resize it, and **right-click the header row** for a menu that ticks each column on
or off (also *Show all columns* and *Reset columns*). The row right-click menu has the same under *Columns*.
Order, widths and hidden columns are saved per grid in ui-state.json and restored at the next start. A column
you have resized keeps its width through the automatic re-sizing that happens after each query; a hidden one
stays hidden.

## Gateways that are slow or overloaded (PA-220)
(v1.4.8 tried an SSH exec channel first; PAN-OS accepts the channel but never answers, so every gateway query
took 90 s to fail and the fallback died with it - removed in v1.5.2. The interactive CLI is the only path.)
A small Palo Alto can take a minute or more to start its CLI after login, and gets slower the more CLI
sessions are opened against it. Since v1.4.7: the gateway query waits up to 2 minutes for the firewall's
prompt and reports plainly when the firewall closed the session before its CLI was ready; the last answer
from each gateway is kept and reused when a later query fails ("Using the N MAC/IP pair(s) it reported at
hh:mm:ss"), so IPs don't go blank; and the IP watch never polls a firewall more often than every 2 minutes,
never while the main run is already talking to it, and backs off (2, 4, 8, 16 min) after failures. Since
v1.4.9, a gateway that fails twice in a row with no cached answer is **parked for the rest of the session**
(logged "parking it for the rest of this session") instead of being retried on every scan - so one overloaded
PA-220 no longer stalls each refresh. Right-click a device row and choose **Retry gateways** once the firewall
has recovered to clear the parked list and query them again.

## Paste ARP / DHCP output (when a firewall is too busy for the app)
**Paste ARP / DHCP output...** (top right of the Connected devices tab, v1.5.0) takes text you copied from your
own SSH session - PAN-OS `show arp all` and/or `show dhcp server lease interface all`, or any switch/router ARP
or DHCP binding table - and fills device IPs from it with the same parsers the gateway query uses
(`(incomplete)` entries are ignored; lease lines add DHCP hostnames). The log shows how many pairs were read
per VLAN. Pasted data is kept for the session and re-applied after every refresh, but only where the live
queries left the IP blank, so live data always wins. *Forget pasted data* drops it.

## Firewalls and APs in the switch list
A Palo Alto firewall listed as a switch (easy to do when a site list is a whole subnet) is now recognised at
login - PAN-OS prompts are `user@host>` and `show system info` answers `model: PA-...` - and removed from the
list the way access points are, with "Palo Alto firewall PA-220 (it is used as a gateway, not listed as a
switch)" in the Status column. The IP watch also leaves alone switches that keep failing (5, 10, 20, 40 min
pauses, max 60) and prints one IP CHANGED line per MAC per poll instead of one per switch that sees it.

## Refreshing the Connected devices tab
**Refresh (re-read switches)** at the top right of the Connected devices tab logs in again to the switches
behind the rows on screen and replaces their rows with what the switches report now (MAC table, LLDP,
PoE/link, 802.1X, IPs via the gateway). Tick some device rows first to refresh only their switches. It runs
the same query as *Get MAC table* on the Switches tab, so expect the same per-switch time.

## Switch links (uplink / downlink) and MAC search
After a query, ports that connect two switches of the loaded list are found from each switch's
LLDP/CDP neighbours (matched by management IP, base MAC or system name; LAG member ports are
mapped to their LAG). The **Switch link** column says `Uplink to <switch>` (blue rows) or
`Downlink to <switch>` (lavender rows). Direction comes from spanning tree: the switch's root port
is its uplink and every other switch link is a downlink (on the root bridge everything is a
downlink). If STP reports nothing, the port where the site gateway's MAC is learned is treated as
the uplink. Each new link is written to the log, e.g. `[sw-a] port 25: Uplink to sw-core`.

**MAC search** (Connected devices tab, under the toolbar): type a full or partial MAC in any format
(`aa:bb:cc:dd:ee:ff`, `aabbcc-ddeeff`, `aabb.ccdd.eeff`, or just `ac:fc`) and press Enter or Find.
Every row with that MAC on every queried switch is selected, the filter is cleared and the groups
expanded, and the result line tells you where the device actually is - the edge port, i.e. the
one that is not a link to another switch (rows on uplinks just show the path to it).
Since v1.4.3 a MAC search also **filters the grid**: only the matching MAC's rows stay visible (the text and
port filters are cleared so nothing hides a hit), and since v1.4.4 only the **edge port** row per MAC - the
sightings of the same MAC on trunks / switch links between switches are hidden (the result line says how
many), unless the MAC was only ever seen on links. Clear the search box or click **Show all** to get the full
list back; a search with no hits also restores it.

## Port filter
Next to the MAC search: type ports in **Ports** to show only those - comma separated, ranges allowed:
`1-24, 49`, `Trk1`, `1/1/1-1/1/12`, `1/1/5-8`, `A1-A4`, `lag1`. Matching is exact - AOS-CX ports are written `1/1/5`.
Click **...** next to the box for a picker that lists every port seen on the queried switches:
radio buttons for *All ports*, *Pick from the list* (tick boxes, with Check all / Check edge ports /
Check switch links and a find box) or *Type ports manually*; the selection is written back to the
box as a compact list such as `1-3, 5, 1/1/7-8, Trk1`. The scope dropdown narrows further: **Edge ports only** hides switch
links (shows where devices are actually plugged in), **Switch links only** shows just the uplinks/
downlinks. Leave the box empty and the dropdown on *All ports* to see everything. A MAC search clears
the port filter if it would hide a hit.

## IP watch
Tick **Watch IP changes (green) every N s** at the bottom of the Connected devices tab. The app then
re-reads the ARP tables on that interval (from the ARP sources, or from the loaded switches if none
are configured) without disturbing anything else. A device whose IP differs from what is shown is
flagged: row in bold green, IP column shows `new IP (was old IP @ time)`, a line in the log, and the
change is listed in the details window. Devices that had no IP yet are filled in quietly.
**Clear marks** removes the marks.

### Proving a VLAN change worked
After **Apply changes** every moved device is tracked and the IP watch is switched on (30 s polls,
for the number of minutes set next to the watch checkbox - default 30). It stops by itself once every
moved device is verified, or when the time is up, in which case the stragglers are marked amber
"NOT verified after N min".
The **VLAN change check** column shows *Waiting: moved to VLAN 132, old IP 10.161.112.55* in orange
until the device gets a new address, then **VERIFIED: 10.161.132.60 is on the VLAN 132 subnet**
in green - or an amber mismatch if the new IP is not in the subnet that VLAN is expected to use.
The expected subnet is learned automatically from devices already on that VLAN and from the ARP
sources' interfaces (`vlan132`, `ethernet1/2.132`); if it can't be inferred, any new IP counts.
If the guess is wrong for a VLAN, set it under **VLAN subnets** on the Settings tab
(`132=10.161.132.0/24`, any prefix length); only listed VLANs are overridden. **Show detected
subnets in log** prints what the app currently believes for every VLAN and whether it was detected
or overridden.
A re-query also notes when the MAC is now learned in the new VLAN. Results are kept across
re-queries until **Clear marks**.

## Changing the access VLAN of ports (e.g. moving phones from 112 to 128)
1. On the Connected devices tab, tick the devices to move. For many devices use
   **Bulk select (MAC list / rules)...**: paste or load a list of MAC addresses (any format) and/or
   set rules such as *vendor contains* `Polycom`, *current access VLAN* `112`, or a specific switch.
   It reports how many devices/ports/switches match and which listed MACs were not found, ticks
   the matches, and **Check and change VLAN...** goes straight into the change dialog.
   Then click **Change access VLAN (checked)...**.
2. The dialog lists one row per switch port with the devices seen on it, the current access VLAN and
   what will happen. LAG ports are always skipped; ports are also skipped when their access VLAN is
   unknown, when they are already on the target VLAN, or when more than one device shares the port
   (tick *Include ports that have more than one device* to allow that). Ports that currently carry
   tagged VLANs are included by default and are turned into plain access ports: untagged on the new
   VLAN, every tagged VLAN removed (ProVision `no tagged` + `untagged`, AOS-CX `vlan access`).
   Untick *Include ports that currently have tagged VLANs* to leave them alone. Exception: a 6000-series
   port configured `vlan trunk native X` + `vlan trunk allowed all` keeps that trunk config and only
   its native VLAN is changed (`vlan trunk native N`). LAG ports are never changed. The app verifies
   the result afterwards.
3. **Validate on switches (no changes)** logs in, checks that the VLAN exists and that each port is a
   plain access port, and reports per port - nothing is modified.
4. **Apply changes...** asks for confirmation, then sends, per switch:
   - ProVision/2530: `configure` / `vlan N` / `untagged <ports>` / `exit` / `exit` / `write memory`
   - AOS-CX/6000: `configure terminal` / `interface X` / `vlan access N` / `exit` ... / `write memory`
   Every command and the switch's reply are written to the log. The changed rows update immediately,
   and a few seconds later the affected switches are queried again automatically so the list shows
   what they now report. Untick *Save configuration* to leave the change unsaved.

The target VLAN must already exist on the switch; the app never creates VLANs.
Devices moved to a new VLAN will need a new IP (DHCP lease) before they are reachable again.

## Excel report by site
**Export all by site (Excel)...** (Connected devices tab) writes an .xlsx with:
- a **Summary** sheet: every switch with its site, subnet, IP, device count, status and last update;
- **one sheet per site**: all devices of the switches in that subnet, with switch name/IP, port,
  MAC, vendor and VLAN, sorted by switch and port, with a header row, filters and frozen title.

A site is the switch IP's subnet (default /24, adjustable on the Settings tab). To give sites real
names, point the Settings tab at a text file with lines like `10.162.30.0/24,Fresno Office`
(see the included `sites.txt`); unmatched switches fall back to their subnet name.

## Settings tab
Connection timing is adjustable (saved automatically to `%LocalAppData%\ArubaMacViewer\settings.json`):
- **SSH connect / login timeout** (default 60 s) – raise it for old, slow 2530 firmware.
- **TCP port 22 probe timeout** (default 5 s) – reachability check before SSH.
- **Command output timeout** (default 90 s) – max wait for one command's output.
- **Quiet period** (default 1500 ms) – output is treated as complete after this much silence.
- **Prefer fast key exchange** – tries fixed DH/ECDH groups before DH group-exchange.
- **Switches queried at the same time** (default 4) – how many SSH sessions run in parallel.
  Set to 1 to go one switch after another.
- **SSH authentication method** – password then keyboard-interactive (default), the reverse, or just
  one of them. Each method the switch rejects counts as a failed login, so pick a single method if
  you see "Too many authentication failures".
- **Alternate username / password** – tried automatically when the main login is rejected. Useful
  when some switches authenticate against RADIUS/AD and others only have a local account. The app
  remembers which set each switch accepted and tries that one first next time. Saved encrypted
  together with the main credentials when **Remember credentials** is ticked.
- **Stop the run after the first login rejection** – on by default so a wrong password is not
  retried on every switch (protects RADIUS/AD accounts from lockout).
- **MAC vendor database** – the IEEE OUI list (~4 MB) is downloaded to
  `%LocalAppData%\ArubaMacViewer\oui.csv` automatically on first run (can be disabled) or via
  **Download / update from IEEE**. Until then a small built-in table covers common vendors.
**Reset to defaults** restores the values above.

## Test SSH (Switches tab)
**Test SSH (checked)** connects to port 22 of the checked switches (or all of them) and reads the
SSH banner - no login, 16 at a time, a few seconds for the whole list. Green means the device speaks
SSH (`SSH-2.0-mocana` = ProVision, `SSH-2.0-OpenSSH` = AOS-CX); red explains the failure (no
answer, port open but no banner, closed without banner, not SSH). Run it on a new list first to
find dead IPs and blocked paths before the full query.

## Troubleshooting
- *"Too many authentication failures"*: the switch closed the session after several failed logins.
  Usually the password is wrong for that switch, or the switch/RADIUS prompt was not answered the
  way it expected. Try **Password only** or **Keyboard-interactive only** on the Settings tab.
- *"SSH negotiation/login timed out"*: the switch answered on port 22 but was too slow to finish the
  SSH key exchange. Old ProVision firmware can take 20-40 s; raise the
  connect timeout on the Settings tab. Updating the firmware makes this much faster.
- *"closed it without an SSH banner"*: the device accepted the TCP connection on port 22 but never
  started SSH. Usually a firewall in the path (Palo Alto App-ID lets the handshake through, then
  blocks SSH), the switch's SSH session limit (stale sessions - `show session-list` / `kill`), an
  `ip authorized-managers` list that excludes your PC, or the IP is not an Aruba switch at all.
  The app retries once automatically.
- *"Cannot reach port 22" / "did not answer"*: the IP is wrong, not routed from your PC, or SSH is
  not enabled on the switch.
- *"No suitable key exchange / host key algorithm"* on an old 2530: update the switch firmware
  (YA/YB 16.x) which adds modern algorithms, or on the switch run `ip ssh` after regenerating the key
  with `crypto key generate ssh rsa bits 2048`.
- *Login fails on ProVision*: make sure a manager password is set (`password manager`) – SSH login
  is not permitted with an empty password.
- *Empty result*: check the log panel at the bottom; the raw parser expects the standard
  `MAC Address / Port / VLAN` table. Run `show mac-address` manually to compare.
