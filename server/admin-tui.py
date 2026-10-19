#!/usr/bin/env python3
"""Meridian 59 server admin console (TUI, mouse supported).

Talks to the local blakserv admin port (127.0.0.1:9998). Run via ./admin-tui.sh
Keys: Ctrl+Q quit, F5 refresh, Tab/Shift+Tab move focus. Everything is also clickable.
"""
import asyncio
import itertools
import os
import json
import re
import shutil
import signal
import socket
import subprocess
import sys
import time

from textual import on
from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.containers import Grid, Horizontal, Vertical
from textual.screen import ModalScreen
from textual.widgets import (Button, DataTable, Footer, Header, Input, Label,
                             RichLog, Select, Static, TabbedContent, TabPane)

HOST = os.environ.get("M59_ADMIN_HOST", "127.0.0.1")
PORT = int(os.environ.get("M59_ADMIN_PORT", "9998"))
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.dirname(HERE)                       # .../m59
GAME_PORT = int(os.environ.get("M59_GAME_PORT", "5959"))
IN_FLATPAK = os.path.exists("/.flatpak-info")      # VS Code sandbox: host tools (distrobox, pkill) need flatpak-spawn --host
IN_BOX = bool(os.environ.get("CONTAINER_ID"))      # already inside the m59-box distrobox


def host_cmd(argv):
    return (["flatpak-spawn", "--host"] if IN_FLATPAK else []) + argv


def port_open(port, host="127.0.0.1"):
    try:
        with socket.create_connection((host, port), timeout=0.5):
            return True
    except OSError:
        return False


def bots_env():
    """Same environment as env.sh: project-local .NET SDK."""
    e = dict(os.environ)
    e.update(DOTNET_ROOT=f"{ROOT}/tools/dotnet", DOTNET_CLI_HOME=f"{ROOT}/tools/dotnet-home",
             DOTNET_CLI_TELEMETRY_OPTOUT="1", NUGET_PACKAGES=f"{ROOT}/tools/nuget")
    e["PATH"] = f"{ROOT}/tools/dotnet:" + e.get("PATH", "")
    return e


ACCOUNT_TYPES = {"A": "admin", "D": "dm", "G": "guest", " ": "user", "": "user"}


class AdminLink:
    """One TCP session to the maintenance port; commands are serialised."""

    def __init__(self):
        self.reader = self.writer = None
        self.lock = asyncio.Lock()
        self._n = itertools.count(1)

    @property
    def connected(self):
        return self.writer is not None and not self.writer.is_closing()

    async def connect(self):
        self.reader, self.writer = await asyncio.wait_for(asyncio.open_connection(HOST, PORT), 3)
        await self._drain(0.3)

    async def close(self):
        if self.writer:
            try:
                self.writer.write(b"quit\r")
                self.writer.close()
            except Exception:
                pass
        self.reader = self.writer = None

    async def _drain(self, idle):
        buf = b""
        while True:
            try:
                d = await asyncio.wait_for(self.reader.read(65536), idle)
            except asyncio.TimeoutError:
                return buf
            if not d:
                raise ConnectionError("server closed the connection")
            buf += d

    async def run(self, cmd, timeout=60):
        try:
            return await self._run(cmd, timeout)
        except (ConnectionError, OSError):
            await self.close()          # dropped (e.g. idle timeout): reconnect once and retry
            return await self._run(cmd, timeout)

    async def _run(self, cmd, timeout):
        """Send a command and return its output. A sentinel command marks the end of the reply
        (the server runs commands in order and echoes '> <command>'); its output lines contain the marker and are dropped."""
        async with self.lock:
            if not self.connected:
                await self.connect()
            marker = f"M59TUI{next(self._n)}END"
            self.writer.write(f"{cmd}\r".encode("latin-1", "replace"))
            self.writer.write(f"show account {marker}\r".encode())   # silent end-of-reply marker (do NOT use "say": it broadcasts to all players)
            await self.writer.drain()
            buf = b""
            end = time.monotonic() + timeout
            while marker.encode() not in buf:
                left = end - time.monotonic()
                if left <= 0:
                    break
                try:
                    d = await asyncio.wait_for(self.reader.read(65536), left)
                except asyncio.TimeoutError:
                    break
                if not d:
                    self.writer = None
                    raise ConnectionError("server closed the connection")
                buf += d
            await self._drain(0.12)
        lines = buf.decode("latin-1").replace("\r", "").split("\n")
        out = [ln for ln in lines if marker not in ln and ln != "Said."]
        # drop the leading echo of our own command and trailing blank lines
        if out and out[0].startswith("> "):
            out = out[1:]
        return "\n".join(out).strip("\n")


class FormModal(ModalScreen):
    """Small generic form: fields = [(key, label, default, kind)] kind: text|password|number|select:a,b,c"""

    BINDINGS = [Binding("escape", "cancel", "Cancel")]

    def __init__(self, title, fields, ok="OK"):
        super().__init__()
        self.title_text, self.fields, self.ok_text = title, fields, ok

    def compose(self) -> ComposeResult:
        with Vertical(id="modal"):
            yield Label(self.title_text, id="modal-title")
            for key, label, default, kind in self.fields:
                yield Label(label)
                if kind.startswith("select:"):
                    opts = [(o, o) for o in kind[7:].split(",")]
                    yield Select(opts, value=default or opts[0][0], allow_blank=False, id=f"f-{key}")
                else:
                    yield Input(value=str(default), password=(kind == "password"),
                                type="integer" if kind == "number" else "text", id=f"f-{key}")
            with Horizontal(id="modal-buttons"):
                yield Button(self.ok_text, variant="primary", id="ok")
                yield Button("Cancel", id="cancel")

    def on_mount(self):
        first = self.query("Input, Select")
        if first:
            first.first().focus()

    @on(Button.Pressed, "#ok")
    def _ok(self):
        self.dismiss({k: str(self.query_one(f"#f-{k}").value) for k, *_ in self.fields})

    @on(Button.Pressed, "#cancel")
    def action_cancel(self):
        self.dismiss(None)

    @on(Input.Submitted)
    def _submit(self):
        self._ok()


class ConfirmModal(ModalScreen):
    BINDINGS = [Binding("escape", "no", "No"), Binding("y", "yes", "Yes"), Binding("n", "no", "No")]

    def __init__(self, text):
        super().__init__()
        self.text = text

    def compose(self) -> ComposeResult:
        with Vertical(id="modal"):
            yield Label(self.text, id="modal-title")
            with Horizontal(id="modal-buttons"):
                yield Button("Yes", variant="error", id="yes")
                yield Button("No", id="no")

    def on_mount(self):
        self.query_one("#no").focus()

    @on(Button.Pressed, "#yes")
    def action_yes(self):
        self.dismiss(True)

    @on(Button.Pressed, "#no")
    def action_no(self):
        self.dismiss(False)


class HistoryInput(Input):
    def __init__(self, *a, **kw):
        super().__init__(*a, **kw)
        self.history, self.pos = [], 0

    def push(self, text):
        if text and (not self.history or self.history[-1] != text):
            self.history.append(text)
        self.pos = len(self.history)

    def on_key(self, event):
        if event.key == "up" and self.history:
            self.pos = max(0, self.pos - 1)
            self.value = self.history[self.pos]
            self.cursor_position = len(self.value)
            event.stop()
        elif event.key == "down" and self.history:
            self.pos = min(len(self.history), self.pos + 1)
            self.value = self.history[self.pos] if self.pos < len(self.history) else ""
            self.cursor_position = len(self.value)
            event.stop()


class AdminApp(App):
    TITLE = "Meridian 59 - Server Admin"
    CSS = """
    #status { height: 3; padding: 0 1; background: $boost; content-align: left middle; }
    #modal { width: 60; height: auto; padding: 1 2; border: thick $primary; background: $surface; }
    FormModal, ConfirmModal { align: center middle; }
    #modal-title { text-style: bold; margin-bottom: 1; }
    #modal-buttons { height: auto; margin-top: 1; align-horizontal: right; }
    #modal-buttons Button { margin-left: 2; }
    .toolbar { height: auto; padding: 0 0 1 0; }
    .toolbar Button { margin-right: 1; }
    RichLog { border: round $primary-darken-2; height: 1fr; }
    DataTable { height: 1fr; border: round $primary-darken-2; }
    #cmd { dock: bottom; }
    #bot-count { width: 9; }
    #tp-filter { width: 28; }
    #tp-char { width: 38; }
    #tp-log { height: 6; }
    .toolbar Label { padding: 1 1 0 0; }
    Grid#server-grid { grid-size: 4; grid-gutter: 1; height: auto; padding-bottom: 1; }
    Grid#server-grid Button { width: 100%; }
    """
    BINDINGS = [
        Binding("ctrl+q", "quit", "Quit"),
        Binding("f5", "refresh_all", "Refresh"),
    ]

    def __init__(self):
        super().__init__()
        self.link = AdminLink()
        self.accounts = {}
        self._was_ok = None
        self.bots_proc = None

    # ---------------------------------------------------------------- layout
    def compose(self) -> ComposeResult:
        yield Header(show_clock=True)
        yield Static("Connecting...", id="status")
        with TabbedContent(initial="tab-console"):
            with TabPane("Console", id="tab-console"):
                with Horizontal(classes="toolbar"):
                    yield Button("Status", id="q-status")
                    yield Button("Who", id="q-who")
                    yield Button("Users", id="q-users")
                    yield Button("Help", id="q-help")
                    yield Button("Clear", id="q-clear")
                yield RichLog(id="log", markup=False, wrap=True, highlight=False)
                yield HistoryInput(placeholder="admin command, e.g. show accounts   (Up/Down = history)", id="cmd")
            with TabPane("Accounts", id="tab-accounts"):
                with Horizontal(classes="toolbar"):
                    yield Button("New account", variant="success", id="a-new")
                    yield Button("New character", id="a-char")
                    yield Button("Suspend", id="a-susp")
                    yield Button("Unsuspend", id="a-unsusp")
                    yield Button("Kick", id="a-kick")
                    yield Button("Delete", variant="error", id="a-del")
                    yield Button("Details", id="a-info")
                    yield Button("Refresh", id="a-refresh")
                yield DataTable(id="acct-table", cursor_type="row", zebra_stripes=True)
            with TabPane("Online", id="tab-online"):
                with Horizontal(classes="toolbar"):
                    yield Button("Refresh", id="o-refresh")
                    yield Button("Hangup session...", id="o-hangup")
                    yield Button("Kick account...", id="o-kick")
                yield RichLog(id="who-log", markup=False, wrap=False)
            with TabPane("Server", id="tab-server"):
                with Horizontal(classes="toolbar"):
                    yield Button("Start server", variant="success", id="srv-start")
                    yield Button("Stop server (saves first)", variant="error", id="srv-stop")
                    yield Button("Restart", variant="warning", id="srv-restart")
                with Grid(id="server-grid"):
                    yield Button("Save game", variant="success", id="s-save")
                    yield Button("Garbage collect", id="s-gc")
                    yield Button("Reload MOTD", id="s-motd")
                    yield Button("Reload packages", id="s-packages")
                    yield Button("Lock game", id="s-lock")
                    yield Button("Unlock game", id="s-unlock")
                    yield Button("Reload system", variant="warning", id="s-reload")
                    yield Button("Kick everyone", variant="error", id="s-kickall")
                    yield Button("Show status", id="s-status")
                    yield Button("Show memory", id="s-memory")
                    yield Button("Show usage", id="s-usage")
                    yield Button("Show config", id="s-config")
                    yield Button("Show timers", id="s-timers")
                    yield Button("Mark logs", id="s-mark")
                    yield Button("Say to admins...", id="s-say")
                yield RichLog(id="srv-log", markup=False, wrap=True)
            with TabPane("Game settings", id="tab-settings"):
                with Horizontal(classes="toolbar"):
                    yield Button("Change...", variant="primary", id="gs-change")
                    yield Button("Refresh", id="gs-refresh")
                yield Static("Select a row, then Change (or press Enter).  Changes apply at once and are saved with the game.", id="gs-info")
                yield DataTable(id="gs-table", cursor_type="row", zebra_stripes=True)
            with TabPane("Teleport", id="tab-tp"):
                with Horizontal(classes="toolbar"):
                    yield Input(placeholder="filter rooms...", id="tp-filter")
                    yield Select([], prompt="character (Refresh users)", id="tp-char", allow_blank=True)
                    yield Button("Refresh users", id="tp-refresh")
                    yield Button("Teleport", variant="success", id="tp-go")
                yield DataTable(id="tp-rooms", cursor_type="row", zebra_stripes=True)
                yield RichLog(id="tp-log", markup=False, wrap=True)
            with TabPane("Bots", id="tab-bots"):
                with Horizontal(classes="toolbar"):
                    yield Label("Bots: ")
                    yield Input(value="20", id="bot-count", type="integer", max_length=3)
                    yield Button("Create accounts", id="b-prov")
                    yield Button("Start bots", variant="success", id="b-start")
                    yield Button("Stop bots", variant="error", id="b-stop")
                    yield Button("Clear log", id="b-clear")
                yield Static("Bots: not running", id="bots-status")
                yield DataTable(id="bot-table", cursor_type="row", zebra_stripes=True)
                yield RichLog(id="bots-log", markup=False, wrap=True, max_lines=500)
        yield Footer()

    async def on_mount(self):
        t = self.query_one("#acct-table", DataTable)
        t.add_columns("ID", "Type", "Name", "Suspended", "Credits", "Last login")
        self.query_one("#bot-table", DataTable).add_columns("Account", "Character", "State", "Rooms", "Moves", "Chats")
        self.query_one("#tp-rooms", DataTable).add_columns("Room", "#", "Map file")
        self.query_one("#gs-table", DataTable).add_columns("Category", "Setting", "Value", "What it does")
        self.settings_id = None
        try:
            self.rooms = json.load(open(os.path.join(HERE, "rooms.json")))
        except Exception as e:
            self.rooms = []
            self.log_line(f"(rooms.json not found: {e} - run tools/make_roomlist.py)", "#tp-log")
        self.fill_rooms()
        self.log_line(f"Connecting to {HOST}:{PORT} ...")
        self.query_one("#cmd").focus()
        self.run_worker(self._initial_load())
        self.set_interval(5, self.refresh_status)

    async def _initial_load(self):
        await self.refresh_status()
        await self.refresh_accounts()
        await self.refresh_settings()

    async def on_unmount(self):
        await self._stop_bots()
        await self.link.close()

    # ---------------------------------------------------------------- game settings (kod Settings object)
    async def refresh_settings(self):
        import gamesettings as gs
        t = self.query_one("#gs-table", DataTable)
        out = await self.cmd("show exactinstances Settings", echo=False)
        m = re.search(r"OBJECT (\d+)", out)
        if not m:
            self.query_one("#gs-info", Static).update("Settings object not found (is the server running?)")
            return
        self.settings_id = int(m.group(1))
        obj = await self.cmd(f"show object {self.settings_id}", echo=False)
        vals = {k: int(v) for k, v in re.findall(r"^:?\s*(\w+)\s*=\s*INT (-?\d+)", obj, re.M)}
        self.setting_values = vals
        keep = t.cursor_row if t.row_count else 0
        t.clear()
        for cat, prop, label, kind, choices, desc in gs.SETTINGS:
            t.add_row(cat, label, gs.fmt_value(kind, choices, vals.get(prop)) + ("" if prop in vals else "  (not in this build)"), desc, key=prop)
        if t.row_count:
            t.move_cursor(row=min(keep, t.row_count - 1))
        self.query_one("#gs-info", Static).update(f"Settings object #{self.settings_id}. Select a row, then Change (or press Enter). Changes apply at once and are saved with the game.")

    @on(Button.Pressed, "#gs-refresh")
    async def _gs_refresh(self):
        await self.refresh_settings()

    @on(DataTable.RowSelected, "#gs-table")
    def _gs_row(self, ev):
        self._gs_change()

    @on(Button.Pressed, "#gs-change")
    def _gs_change(self):
        import gamesettings as gs
        t = self.query_one("#gs-table", DataTable)
        if not t.row_count or self.settings_id is None:
            self.notify("Press Refresh first (server must be running)", severity="warning")
            return
        prop = t.coordinate_to_cell_key(t.cursor_coordinate).row_key.value
        cat, _, label, kind, choices, desc = next(x for x in gs.SETTINGS if x[1] == prop)
        cur = self.setting_values.get(prop, 0)
        if kind == "choice":
            opts = ",".join(f"{k} = {v}" for k, v in choices.items())
            k0 = cur if cur in choices else next(iter(choices))
            field = ("v", f"{label}", f"{k0} = {choices[k0]}", "select:" + opts)
        elif kind == "bool":
            field = ("v", label, "On" if cur else "Off", "select:On,Off")
        else:
            field = ("v", label, str(cur), "number")

        def done(r):
            if r is not None:
                self.run_worker(self._gs_apply(prop, kind, r["v"]))
        self.push_screen(FormModal(label, [field], "Apply"), done)

    async def _gs_apply(self, prop, kind, text):
        if kind == "choice":
            val = int(text.split("=")[0].strip())
        elif kind == "bool":
            val = 1 if text == "On" else 0
        else:
            try:
                val = int(text)
            except ValueError:
                self.notify("Enter a whole number", severity="error")
                return
        out = await self.cmd(f"set object {self.settings_id} {prop} int {val}")
        await self.cmd("save game", echo=False)
        self.notify(f"{prop} = {val}" if "rror" not in out else "Failed - see Console", severity="information" if "rror" not in out else "error")
        await self.refresh_settings()

    # ---------------------------------------------------------------- helpers
    def log_line(self, text, widget="#log"):
        self.query_one(widget, RichLog).write(text)

    async def cmd(self, command, widget="#log", echo=True):
        """Run an admin command, write the result to a log, return the text."""
        if echo:
            self.log_line(f"> {command}", widget)
        try:
            out = await self.link.run(command)
        except Exception as e:
            self.log_line(f"[connection error] {e}", widget)
            self.set_status(False, str(e))
            return ""
        if out:
            self.log_line(out, widget)
        return out

    def set_status(self, ok, extra=""):
        if ok != self._was_ok:
            self._was_ok = ok
            self.log_line(f"Connected to {HOST}:{PORT}." if ok else f"Disconnected from {HOST}:{PORT}: {extra} (retrying every 5s)")
        w = self.query_one("#status", Static)
        w.update(("[green]● connected[/]" if ok else "[red]● disconnected[/]") + f"  {HOST}:{PORT}  {extra}")

    async def refresh_status(self):
        try:
            out = await self.link.run("show status")
        except Exception as e:
            self.set_status(False, f"({e})")
            return
        up = re.search(r"up for ([^\n(=]+)", out)
        acc = re.search(r"Active accounts: (\d+)", out)
        ses = re.search(r"(\d+) sessions? logged on", out)
        ver = re.search(r"BlakSton Server (v[\d.]+)", out)
        self.set_status(True, "  ".join(x for x in [
            ver and f"server {ver.group(1)}", up and f"up {up.group(1).strip()}",
            acc and f"{acc.group(1)} accounts", ses and f"{ses.group(1)} sessions"] if x))

    async def refresh_accounts(self):
        try:
            out = await self.link.run("show accounts")
        except Exception:
            return
        t = self.query_one("#acct-table", DataTable)
        t.clear()
        self.accounts = {}
        lines = out.split("\n")
        hdr = next((l for l in lines if l.startswith("Acct")), None)
        if not hdr:
            return
        i_name, i_susp = hdr.index("Name"), hdr.index("Suspended")
        i_cred, i_last = hdr.index("Credits"), hdr.index("Last login")
        for l in lines[lines.index(hdr) + 1:]:
            m = re.match(r"\s*(\d+)(\S?)\s", l)
            if not m:
                continue
            aid, typ = int(m.group(1)), m.group(2)
            name, susp = l[i_name:i_susp].strip(), l[i_susp:i_cred].strip()
            cred, last = l[i_cred:i_last].strip(), l[i_last:].strip()
            self.accounts[aid] = name
            t.add_row(str(aid), ACCOUNT_TYPES.get(typ, typ), name, susp or "-", cred, last, key=str(aid))

    def selected_account(self):
        t = self.query_one("#acct-table", DataTable)
        if t.row_count == 0:
            self.notify("No account selected", severity="warning")
            return None
        try:
            key = t.coordinate_to_cell_key(t.cursor_coordinate).row_key.value
            return int(key)
        except Exception:
            self.notify("Select an account row first", severity="warning")
            return None

    async def refresh_who(self):
        w = self.query_one("#who-log", RichLog)
        w.clear()
        out = await self.link.run("who")
        w.write(out or "(nobody)")
        w.write("")
        w.write(await self.link.run("show users") or "")

    async def action_refresh_all(self):
        await self.refresh_status()
        await self.refresh_accounts()
        await self.refresh_who()

    # ---------------------------------------------------------------- console tab
    @on(Input.Submitted, "#cmd")
    async def _cmd_submit(self, ev):
        text = ev.value.strip()
        ev.input.value = ""
        if not text:
            return
        ev.input.push(text)
        await self.cmd(text)
        if text.split()[0].lower() in ("create", "delete", "suspend", "unsuspend", "set"):
            await self.refresh_accounts()

    @on(Button.Pressed, "#q-status")
    async def _q_status(self):
        await self.cmd("show status")

    @on(Button.Pressed, "#q-who")
    async def _q_who(self):
        await self.cmd("who")

    @on(Button.Pressed, "#q-users")
    async def _q_users(self):
        await self.cmd("show users")

    @on(Button.Pressed, "#q-help")
    async def _q_help(self):
        await self.cmd("help")

    @on(Button.Pressed, "#q-clear")
    def _q_clear(self):
        self.query_one("#log", RichLog).clear()

    # ---------------------------------------------------------------- accounts tab
    @on(Button.Pressed, "#a-refresh")
    async def _a_refresh(self):
        await self.refresh_accounts()

    @on(Button.Pressed, "#a-new")
    def _a_new(self):
        def done(r):
            if r:
                self.run_worker(self._create_account(r))
        self.push_screen(FormModal("New account", [
            ("name", "Account name", "", "text"),
            ("pw", "Password", "", "password"),
            ("type", "Account type", "user", "select:user,admin,dm"),
            ("char", "Also create a character?", "yes", "select:yes,no"),
        ], "Create"), done)

    async def _create_account(self, r):
        name, pw, typ = r["name"].strip(), r["pw"], r["type"]
        if not name or not pw:
            self.notify("Name and password are required", severity="error")
            return
        out = await self.cmd(f"create account {typ} {name} {pw}", echo=False)
        self.log_line(f"> create account {typ} {name} ****")
        self.log_line(out)
        m = re.search(r"ACCOUNT (\d+)", out)
        if m and r["char"] == "yes":
            await self.cmd(f"create {typ} {m.group(1)}")
        await self.cmd("save game")
        await self.refresh_accounts()
        self.notify(f"Account '{name}' created" if m else "Create failed - see Console", severity="information" if m else "error")

    @on(Button.Pressed, "#a-char")
    def _a_char(self):
        aid = self.selected_account()
        if aid is None:
            return

        def done(r):
            if r:
                self.run_worker(self._create_char(aid, r["type"]))
        self.push_screen(FormModal(f"New character for account {aid} ({self.accounts.get(aid, '?')})",
                                   [("type", "Character type", "user", "select:user,admin,dm")], "Create"), done)

    async def _create_char(self, aid, typ):
        await self.cmd(f"create {typ} {aid}")
        await self.cmd("save game")
        await self.refresh_accounts()

    @on(Button.Pressed, "#a-susp")
    def _a_susp(self):
        aid = self.selected_account()
        if aid is None:
            return

        def done(r):
            if r:
                self.run_worker(self._simple(f"suspend account {aid} {int(r['hours'] or 1)}", refresh=True))
        self.push_screen(FormModal(f"Suspend account {aid}", [("hours", "Hours", "24", "number")], "Suspend"), done)

    @on(Button.Pressed, "#a-unsusp")
    async def _a_unsusp(self):
        aid = self.selected_account()
        if aid is not None:
            await self._simple(f"unsuspend account {aid}", refresh=True)

    @on(Button.Pressed, "#a-kick")
    async def _a_kick(self):
        aid = self.selected_account()
        if aid is not None:
            await self._simple(f"kickoff account {aid}")

    @on(Button.Pressed, "#a-info")
    async def _a_info(self):
        aid = self.selected_account()
        if aid is not None:
            self.query_one(TabbedContent).active = "tab-console"
            await self.cmd(f"show account {aid}")

    @on(Button.Pressed, "#a-del")
    def _a_del(self):
        aid = self.selected_account()
        if aid is None:
            return

        def done(yes):
            if yes:
                self.run_worker(self._delete_account(aid))
        self.push_screen(ConfirmModal(f"Delete account {aid} ({self.accounts.get(aid, '?')}) and its characters?"), done)

    async def _delete_account(self, aid):
        await self.cmd(f"delete account {aid}")
        await self.cmd("save game")
        await self.refresh_accounts()

    async def _simple(self, command, refresh=False, widget="#log"):
        out = await self.cmd(command, widget)
        if refresh:
            await self.refresh_accounts()
        return out

    # ---------------------------------------------------------------- online tab
    @on(Button.Pressed, "#o-refresh")
    async def _o_refresh(self):
        await self.refresh_who()

    @on(Button.Pressed, "#o-hangup")
    def _o_hangup(self):
        def done(r):
            if r and r["id"].strip():
                self.run_worker(self._simple(f"hangup session {int(r['id'])}", widget="#who-log"))
        self.push_screen(FormModal("Hang up session", [("id", "Session number (from 'Sess' column)", "", "number")], "Hangup"), done)

    @on(Button.Pressed, "#o-kick")
    def _o_kick(self):
        def done(r):
            if r and r["id"].strip():
                self.run_worker(self._simple(f"kickoff account {int(r['id'])}", widget="#who-log"))
        self.push_screen(FormModal("Kick account out of the game", [("id", "Account number", "", "number")], "Kick"), done)

    # ---------------------------------------------------------------- server tab
    SERVER_CMDS = {
        "s-save": "save game", "s-gc": "garbage", "s-motd": "reload motd", "s-packages": "reload packages",
        "s-lock": "lock Server locked by admin console", "s-unlock": "unlock", "s-status": "show status",
        "s-memory": "show memory", "s-usage": "show usage", "s-config": "show configuration",
        "s-timers": "show timers", "s-mark": "mark",
    }

    @on(Button.Pressed, "#server-grid Button")
    def _server_btn(self, ev):
        bid = ev.button.id
        if bid in self.SERVER_CMDS:
            self.run_worker(self._simple(self.SERVER_CMDS[bid], widget="#srv-log"))
        elif bid == "s-reload":
            def done(yes):
                if yes:
                    self.run_worker(self._simple("reload system", widget="#srv-log"))
            self.push_screen(ConfirmModal("Save the game and reload ALL kod? Players will be disconnected."), done)
        elif bid == "s-kickall":
            def done2(yes):
                if yes:
                    self.run_worker(self._simple("kickoff all", widget="#srv-log"))
            self.push_screen(ConfirmModal("Kick every player out of the game?"), done2)
        elif bid == "s-say":
            def done3(r):
                if r and r["text"].strip():
                    self.run_worker(self._simple(f"say {r['text']}", widget="#srv-log"))
            self.push_screen(FormModal("Say to all admins", [("text", "Text", "", "text")], "Send"), done3)

    # ---------------------------------------------------------------- server start / stop
    def srv_log(self, text):
        self.log_line(text, "#srv-log")

    @on(Button.Pressed, "#srv-start")
    async def _srv_start_btn(self):
        await self.start_server()

    @on(Button.Pressed, "#srv-stop")
    async def _srv_stop_btn(self):
        await self.stop_server()

    @on(Button.Pressed, "#srv-restart")
    async def _srv_restart_btn(self):
        await self.stop_server()
        await self.start_server()

    async def start_server(self):
        if port_open(GAME_PORT):
            self.srv_log("Server already running (game port open).")
            return
        run = f"cd '{HERE}' && exec ./blakserv </dev/null >console.out 2>&1"
        argv = ["bash", "-c", run] if IN_BOX else host_cmd(["distrobox", "enter", "m59-box", "--", "bash", "-c", run])
        self.srv_log("Starting server ...")
        try:
            subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        except OSError as e:
            self.srv_log(f"Could not start: {e}")
            return
        for _ in range(60):                       # up to 30 s
            await asyncio.sleep(0.5)
            if port_open(GAME_PORT):
                break
        else:
            self.srv_log("Server did not open its game port within 30 s - see server/console.out")
            return
        await asyncio.sleep(10)                   # the port opens before the kod world has finished loading
        await self.refresh_status()
        await self.refresh_accounts()
        self.srv_log("Server started.")
        self.notify("Server started")

    async def stop_server(self):
        if not port_open(GAME_PORT) and not port_open(PORT):
            self.srv_log("Server is not running.")
            return
        await self._stop_bots()
        self.srv_log("Saving game ...")
        try:
            await self.link.run("save game")
        except Exception as e:
            self.srv_log(f"(save failed: {e})")
        subprocess.run(host_cmd(["pkill", "-x", "blakserv"]), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(40):
            await asyncio.sleep(0.5)
            if not port_open(GAME_PORT):
                break
        await self.link.close()
        self.srv_log("Server stopped." if not port_open(GAME_PORT) else "Server still running?")
        await self.refresh_status()

    # ---------------------------------------------------------------- teleport tab
    def fill_rooms(self):
        t = self.query_one("#tp-rooms", DataTable)
        t.clear()
        f = self.query_one("#tp-filter", Input).value.strip().lower()
        for r in self.rooms:
            if f and f not in r["name"].lower() and f != str(r["rid"]) and f not in (r.get("file") or "").lower():
                continue
            t.add_row(r["name"], str(r["rid"]), r.get("file") or "", key=str(r["rid"]))

    @on(Input.Changed, "#tp-filter")
    def _tp_filter(self):
        self.fill_rooms()

    @on(Button.Pressed, "#tp-refresh")
    async def _tp_refresh(self):
        out = await self.cmd("show users", widget="#tp-log", echo=False)
        opts = []
        for line in out.split("\n"):
            m = re.match(r"^\s*(\d+)\s+(\d+)\s+(\S+)\s+(.+?)\s*$", line)
            if m:
                opts.append((f"{m.group(4)}  (obj {m.group(2)}, acct {m.group(1)})", m.group(2)))
        self.query_one("#tp-char", Select).set_options(opts)
        self.log_line(f"{len(opts)} characters (only logged-in ones can be teleported)", "#tp-log")

    @on(Button.Pressed, "#tp-go")
    async def _tp_go(self):
        sel = self.query_one("#tp-char", Select).value
        t = self.query_one("#tp-rooms", DataTable)
        if sel is Select.BLANK or sel is None or t.row_count == 0:
            self.notify("Pick a character (Refresh users) and a room first", severity="warning")
            return
        rid = t.coordinate_to_cell_key(t.cursor_coordinate).row_key.value
        name = next((r["name"] for r in self.rooms if str(r["rid"]) == rid), rid)
        out = await self.cmd(f"send object {sel} TeleportTo RID int {rid}", widget="#tp-log")
        ok = "INT 1" in out
        self.notify(f"Teleported to {name}" if ok else "Failed (character not logged in, or bad room?)", severity="information" if ok else "error")

    # ---------------------------------------------------------------- bots tab
    def bots_log(self, text):
        self.log_line(text, "#bots-log")

    def bot_count(self):
        try:
            return max(1, min(500, int(self.query_one("#bot-count", Input).value)))
        except ValueError:
            return 20

    @on(Button.Pressed, "#b-clear")
    def _b_clear(self):
        self.query_one("#bots-log", RichLog).clear()

    @on(Button.Pressed, "#b-prov")
    async def _b_prov(self):
        n = self.bot_count()
        if not port_open(PORT):
            self.bots_log("Server admin port is not reachable - start the server first.")
            return
        self.bots_log(f"Creating up to {n} bot accounts (bot001...) ...")
        p = await asyncio.create_subprocess_exec(sys.executable, f"{ROOT}/bots/provision.py", str(n),
                                                 stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
        out, _ = await p.communicate()
        self.bots_log(out.decode().strip())
        await self.refresh_accounts()

    @on(Button.Pressed, "#b-start")
    async def _b_start(self):
        if self.bots_proc and self.bots_proc.returncode is None:
            self.bots_log("Bots are already running - stop them first.")
            return
        if not port_open(GAME_PORT):
            self.bots_log("Server is not running (game port closed). Start it on the Server tab.")
            return
        n = self.bot_count()
        env = bots_env()
        dotnet = f"{ROOT}/tools/dotnet/dotnet"
        self.bots_log("Building bots ...")
        b = await asyncio.create_subprocess_exec(dotnet, "build", "-v", "q", cwd=f"{ROOT}/bots", env=env,
                                                 stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
        out, _ = await b.communicate()
        if b.returncode != 0:
            self.bots_log(out.decode()[-1500:])
            self.bots_log("Build failed.")
            return
        self.bots_log(f"Starting {n} bots ...")
        self.bots_proc = await asyncio.create_subprocess_exec(
            dotnet, f"{ROOT}/bots/bin/Debug/net8.0/Bots.dll", "--count", str(n), "--minutes", "0", "--stagger", "0.3", "--rows",
            cwd=f"{ROOT}/bots", env=env, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
        self.run_worker(self._read_bots(self.bots_proc))

    async def _read_bots(self, proc):
        table = self.query_one("#bot-table", DataTable)
        rows = {}
        async for raw in proc.stdout:
            line = raw.decode(errors="replace").rstrip()
            if line.startswith("SUM|"):
                _, total, playing, failed, moves, chats, cpu, mem = line.split("|")
                self.query_one("#bots-status", Static).update(
                    f"Bots: [green]{playing} playing[/] of {total}   failed {failed}   moves {moves}   chats {chats}   bot process {cpu}% CPU, {mem} MB")
            elif line.startswith("BOT|"):
                _, acct, char, state, rooms_, moves, chats = line.split("|")
                vals = (acct, char or "-", state, rooms_, moves, chats)
                if acct in rows:
                    for col, v in zip(table.columns.keys(), vals):
                        table.update_cell(rows[acct], col, v)
                else:
                    rows[acct] = table.add_row(*vals, key=acct)
            elif line:
                self.bots_log(line)
        rc = await proc.wait()
        self.bots_log(f"Bot process ended (exit {rc}).")
        self.query_one("#bots-status", Static).update("Bots: not running")
        table.clear()

    async def _stop_bots(self):
        p = self.bots_proc
        if p and p.returncode is None:
            self.bots_log("Stopping bots (logging them out) ...")
            p.send_signal(signal.SIGTERM)
            try:
                await asyncio.wait_for(p.wait(), 8)
            except asyncio.TimeoutError:
                p.kill()

    @on(Button.Pressed, "#b-stop")
    async def _b_stop(self):
        if not (self.bots_proc and self.bots_proc.returncode is None):
            self.bots_log("Bots are not running.")
            return
        await self._stop_bots()


if __name__ == "__main__":
    AdminApp().run()
