import os
import asyncio, sys, importlib.util
spec = importlib.util.spec_from_file_location("tui", os.path.join(os.path.dirname(os.path.abspath(__file__)), "../server/admin-tui.py"))
tui = importlib.util.module_from_spec(spec); spec.loader.exec_module(tui)
from textual.widgets import DataTable, RichLog, Static, TabbedContent

async def main():
    app = tui.AdminApp()
    async with app.run_test(size=(120, 40)) as pilot:
        await pilot.pause(2.0)
        st = str(app.query_one("#status", Static).render())
        print("STATUS:", st)
        t = app.query_one("#acct-table", DataTable)
        print("ACCOUNT ROWS:", t.row_count, [t.get_row_at(i) for i in range(t.row_count)])
        # type a command in console
        await pilot.click("#cmd"); await pilot.press(*"show clock", "enter"); await pilot.pause(1.5)
        log = app.query_one("#log", RichLog)
        print("LOG LINES:", [l.text for l in log.lines][-4:])
        # click through tabs / buttons
        app.query_one(TabbedContent).active = "tab-online"; await pilot.pause(0.2)
        await pilot.pause(0.5); app.query_one("#o-refresh").press(); await pilot.pause(3)
        print("WHO:", [l.text for l in app.query_one("#who-log", RichLog).lines][:4])
        app.query_one(TabbedContent).active = "tab-server"; await pilot.pause(0.2)
        app.query_one(TabbedContent).active = "tab-server"; await pilot.pause(0.3); await pilot.pause(0.5); app.query_one("#s-status").press(); await pilot.pause(3)
        print("SRV:", [l.text for l in app.query_one("#srv-log", RichLog).lines][:3])
        app.save_screenshot(os.path.join(os.path.dirname(os.path.abspath(__file__)), "tui_shot.svg"))
asyncio.run(main())
