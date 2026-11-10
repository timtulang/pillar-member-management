"""End-to-end tests against a running app and disposable PostgreSQL database.
Use only synthetic accounts. No QR credentials, passwords, or TOTP secrets are printed.
"""
import base64
import hashlib
import hmac
import os
import re
import secrets
import struct
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import urlparse

import psycopg
from playwright.sync_api import sync_playwright, expect
from psycopg.conninfo import conninfo_to_dict

BASE = os.environ.get("PILLAR_TEST_URL", "http://127.0.0.1:5080").rstrip("/")
DSN = os.environ["PILLAR_TEST_DATABASE"]
EMAIL = os.environ["PILLAR_TEST_ADMIN_EMAIL"]
PASSWORD = os.environ["PILLAR_TEST_ADMIN_PASSWORD"]
SECRET = os.environ.get("PILLAR_TEST_TOTP_SECRET")
ARTIFACTS = Path(os.environ.get("PILLAR_TEST_ARTIFACTS", "artifacts/test"))
ARTIFACTS.mkdir(parents=True, exist_ok=True)
assert urlparse(BASE).hostname in ("localhost", "127.0.0.1"), "Tests require a local disposable app."
assert conninfo_to_dict(DSN).get("host") in ("127.0.0.1", "localhost"), "Tests require a local disposable database."
RUN = secrets.token_hex(4)


def totp(secret):
    raw = hmac.new(base64.b32decode(secret.upper()), struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
    offset = raw[-1] & 15
    return f"{(struct.unpack('>I', raw[offset:offset+4])[0] & 0x7fffffff) % 1000000:06d}"


def query(sql, args=(), one=False):
    with psycopg.connect(DSN) as c:
        result = c.execute(sql, args)
        return result.fetchone() if one else result.fetchall()


def sign_in(page, email, password, secret=None):
    page.goto(BASE + "/Account/Login")
    page.get_by_label("Email", exact=True).fill(email)
    page.get_by_label("Password", exact=True).fill(password)
    page.get_by_role("button", name=re.compile("^Sign in")).click()
    if "/Account/Mfa" in page.url:
        secret = page.locator("code.wrap").text_content().strip()
        page.get_by_label("Code", exact=True).fill(totp(secret))
        page.get_by_role("button", name="Enable authenticator").click()
        assert "enabled" in page.locator(".notice").inner_text()
        assert page.locator(".recovery-codes code").count() == 10
        page.goto(BASE + "/Events")
    elif "/Account/Verify" in page.url:
        assert secret, "Supply the synthetic account's TOTP secret."
        page.get_by_label("Code", exact=True).fill(totp(secret))
        page.get_by_role("button", name="Verify and sign in").click()
    assert "/Account/" not in page.url
    return secret


def post(page, path, values, follow=False):
    csrf = page.locator('[name="__RequestVerificationToken"]').first.get_attribute("value")
    return page.request.post(BASE + path, form={"__RequestVerificationToken": csrf, **values}, max_redirects=10 if follow else 0)


def success(response):
    assert response.status in (200, 302), f"Unexpected status {response.status}"


def create_member(page, first, surname):
    page.goto(BASE + "/Members/Edit")
    page.get_by_label("First name", exact=True).fill(first)
    page.get_by_label("Surname", exact=True).fill(surname)
    page.get_by_label("Birthday", exact=True).fill("1995-03-14")
    page.get_by_label("Church status").select_option("member")
    page.get_by_role("button", name="Save member", exact=True).click()
    assert "/Members/Details/" in page.url, "Member creation failed"
    assert "couldn't finish" not in page.locator("h1").inner_text()
    return query("SELECT id,member_code FROM pillar.people WHERE first_name=%s AND surname=%s", (first, surname), True)


def main():
    with sync_playwright() as pw:
        browser = pw.chromium.launch(executable_path=os.environ.get("CHROMIUM_PATH", "/usr/bin/chromium"), headless=True, args=["--no-sandbox"])
        context = browser.new_context(viewport={"width": 1440, "height": 1000})
        page = context.new_page()
        page.set_default_timeout(12000)
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        sign_in(page, EMAIL, PASSWORD, SECRET)
        page.goto(BASE + "/")
        assert "Your church, connected." in page.locator("h1").inner_text()
        print("PASS staff login and authenticator MFA", flush=True)

        father = create_member(page, "Roberto", "Test-" + RUN)
        son = create_member(page, "Daniel", "Test-" + RUN)
        spouse = create_member(page, "Maria", "Test-" + RUN)
        page.goto(BASE + "/Families")
        success(post(page, "/Families", {"name": "Original family " + RUN}))
        success(post(page, "/Families", {"name": "New family " + RUN}))
        old = query("SELECT id FROM pillar.households WHERE display_name=%s", ("Original family " + RUN,), True)[0]
        new = query("SELECT id FROM pillar.households WHERE display_name=%s", ("New family " + RUN,), True)[0]
        detail = f"/Members/Details/{son[0]}"
        page.goto(BASE + detail)
        success(post(page, detail + "?handler=Move", {"householdId": str(old), "role": "child", "effective": "2020-01-01"}))
        success(post(page, detail + "?handler=Parent", {"relativeCode": father[1], "direction": "parent", "kind": "biological"}))
        success(post(page, detail + "?handler=Marriage", {"spouseCode": spouse[1], "marriedOn": "2026-01-01"}))
        success(post(page, detail + "?handler=Move", {"householdId": str(new), "role": "adult", "effective": "2026-02-01"}))
        history = query("SELECT household_id,valid_to FROM pillar.household_memberships WHERE person_id=%s ORDER BY valid_from", (son[0],))
        assert len(history) == 2 and history[0][1].isoformat() == "2026-02-01" and history[1][1] is None
        assert query("SELECT count(*) FROM pillar.parent_child_relationships WHERE parent_id=%s AND child_id=%s AND valid_to IS NULL", (father[0], son[0]), True)[0] == 1
        assert query("SELECT count(*) FROM pillar.marriages WHERE %s IN(spouse_a_id,spouse_b_id)", (son[0],), True)[0] == 1
        page.goto(BASE + detail)
        expect(page.locator(".timeline-item").filter(has_text="Original family")).to_contain_text("01 Jan 2020 → 01 Feb 2026")
        expect(page.locator(".timeline-item").filter(has_text="New family")).to_contain_text("01 Feb 2026 → Ongoing")
        expect(page.locator(".relationship").filter(has_text="Married")).to_contain_text("01 Jan 2026")
        print("PASS son-to-spouse transition preserves parentage and household history", flush=True)

        # Optimistic concurrency: two editors cannot silently overwrite a profile.
        page.goto(BASE + f"/Members/Edit/{son[0]}")
        assert page.get_by_label("Birthday", exact=True).input_value() == "1995-03-14"
        stale_version = page.locator('[name="Input.Version"]').input_value()
        query("UPDATE pillar.people SET preferred_name='Danny' WHERE id=%s RETURNING id", (son[0],))
        page.get_by_label("Preferred name").fill("Stale change")
        page.get_by_role("button", name="Save member", exact=True).click()
        assert "changed while you were editing" in page.locator(".validation").inner_text()
        assert query("SELECT preferred_name FROM pillar.people WHERE id=%s", (son[0],), True)[0] == "Danny"
        print("PASS stale edits are rejected", flush=True)

        page.goto(BASE + detail)
        ministry = query("SELECT id FROM pillar.ministries WHERE name='Music'", one=True)[0]
        program = query("SELECT id FROM pillar.discipleship_programs ORDER BY name LIMIT 1", one=True)[0]
        success(post(page, detail + "?handler=Ministry", {"ministryId": str(ministry), "main": "true", "since": "2026-01-01"}))
        success(post(page, detail + "?handler=Progress", {"programId": str(program), "status": "in_progress", "startedOn": "2026-01-01"}))
        success(post(page, detail + "?handler=Baptism", {"location": "pillar"}))
        assert query("SELECT count(*) FROM pillar.ministry_assignments WHERE person_id=%s", (son[0],), True)[0] == 1
        assert query("SELECT count(*) FROM pillar.discipleship_enrollments WHERE person_id=%s", (son[0],), True)[0] == 1
        print("PASS ministry, discipleship, and baptism records", flush=True)

        page.goto(BASE + "/Events")
        now = datetime.now(timezone.utc)
        success(post(page, "/Events", {"title": "Test gathering " + RUN, "startsAt": (now - timedelta(minutes=15)).strftime("%Y-%m-%dT%H:%M"), "endsAt": (now + timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M"), "timeZone": "UTC", "minutesBefore": "60", "minutesAfter": "30"}))
        event = query("SELECT id FROM pillar.event_occurrences WHERE title=%s", ("Test gathering " + RUN,), True)[0]
        event_path = f"/Events/Details/{event}"
        page.goto(BASE + f"/Members/Card/{son[0]}")
        page.get_by_role("button", name="Issue printable ID").click()
        qr_image = page.locator(".member-card img").get_attribute("src")
        assert qr_image.startswith("data:image/png;base64,")
        page.emulate_media(media="print")
        assert page.locator(".sidebar").is_hidden()
        page.emulate_media(media="screen")
        page.goto(BASE + event_path)
        # Decode the actual issued image, then feed that image through the live camera loop.
        token = page.evaluate("""async source => { const image = new Image(); image.src = source; await image.decode(); const c = document.createElement('canvas'); c.width=image.width; c.height=image.height; const ctx=c.getContext('2d'); ctx.drawImage(image,0,0); const data=ctx.getImageData(0,0,c.width,c.height); return jsQR(data.data,c.width,c.height).data; }""", qr_image)
        await_camera = """async source => { const image = new Image(); image.src=source; await image.decode(); navigator.mediaDevices.getUserMedia = async () => { const c=document.createElement('canvas'); c.width=640; c.height=480; const ctx=c.getContext('2d'); const draw=()=>{ctx.fillStyle='white';ctx.fillRect(0,0,640,480);ctx.drawImage(image,150,70,340,340);}; draw();setInterval(draw,100);return c.captureStream(10); }; }"""
        page.evaluate(await_camera, qr_image)
        page.get_by_role("button", name="Start camera").click()
        expect(page.locator("#scan-status")).to_contain_text("Checked in successfully", timeout=12000)
        page.get_by_role("button", name="Stop", exact=True).click()
        assert query("SELECT count(*) FROM pillar.attendance_records WHERE event_occurrence_id=%s", (event,), True)[0] == 1
        assert post(page, event_path + "?handler=Scan", {"token": token}).json()["duplicate"] is True
        csrf = page.locator('[name="__RequestVerificationToken"]').first.get_attribute("value")
        assert page.request.post(BASE + event_path + "?handler=Scan", form={"token": token}, max_redirects=0).status == 400
        # Browser fetches are concurrent, using the same valid cookie/CSRF context.
        results = page.evaluate("""async ({path,token,csrf}) => await Promise.all(Array.from({length:8},async()=>{const r=await fetch(path,{method:'POST',body:new URLSearchParams({token,__RequestVerificationToken:csrf})});return r.status;}))""", {"path": event_path + "?handler=Scan", "token": token, "csrf": csrf})
        assert results == [200] * 8
        assert query("SELECT count(*) FROM pillar.attendance_records WHERE event_occurrence_id=%s", (event,), True)[0] == 1
        print("PASS camera decoding, printable QR, duplicate/concurrent scans, and CSRF rejection", flush=True)

        attendance_id = query("SELECT id FROM pillar.attendance_records WHERE event_occurrence_id=%s", (event,), True)[0]
        success(post(page, event_path + "?handler=Correct", {"attendanceId": str(attendance_id), "restore": "false", "reason": "Synthetic correction"}))
        assert post(page, event_path + "?handler=Scan", {"token": token}).status == 400
        success(post(page, event_path + "?handler=Correct", {"attendanceId": str(attendance_id), "restore": "true", "reason": "Synthetic reinstatement"}))
        assert post(page, event_path + "?handler=Scan", {"token": token}).status == 200
        page.goto(BASE + f"/Members/Card/{son[0]}")
        page.get_by_role("button", name="Issue printable ID").click()
        replacement_qr = page.locator(".member-card img").get_attribute("src")
        page.goto(BASE + event_path)
        assert post(page, event_path + "?handler=Scan", {"token": token}).status == 400
        replacement_token = page.evaluate("""async source => { const image=new Image(); image.src=source; await image.decode(); const c=document.createElement('canvas'); c.width=image.width;c.height=image.height;const ctx=c.getContext('2d');ctx.drawImage(image,0,0);const data=ctx.getImageData(0,0,c.width,c.height);return jsQR(data.data,c.width,c.height).data; }""", replacement_qr)
        query("UPDATE pillar.member_cards SET issued_at=now()-interval '2 days',expires_at=now()-interval '1 day' WHERE person_id=%s AND revoked_at IS NULL RETURNING id", (son[0],))
        assert post(page, event_path + "?handler=Scan", {"token": replacement_token}).status == 400
        assert post(page, event_path + "?handler=Scan", {"token": "not-a-member-token"}).status == 400
        success(post(page, event_path + "?handler=Manual", {"memberCode": spouse[1], "reason": "Forgot card"}))
        assert query("SELECT count(*) FROM pillar.attendance_records WHERE event_occurrence_id=%s AND voided_at IS NULL", (event,), True)[0] == 2
        print("PASS void/reinstate, card revocation/expiry, invalid tokens, and manual check-in", flush=True)

        # Provision an attendance-only staff member and verify server-side access denial.
        operator_email = f"operator-{RUN}@example.test"
        operator_password = "Operator!" + secrets.token_urlsafe(24)
        page.goto(BASE + "/Staff")
        success(post(page, "/Staff?handler=Create", {"email": operator_email, "password": operator_password, "role": "attendance_operator"}))
        restricted = browser.new_context()
        op = restricted.new_page()
        operator_secret = sign_in(op, operator_email, operator_password)
        for path in ("/Members", detail, "/Families", "/Staff", "/Staff/Audit", "/Ministries"):
            response = op.request.get(BASE + path)
            assert response.status == 403, f"Operator accessed a restricted route: {path}"
        op.goto(BASE + event_path)
        assert post(op, event_path + "?handler=Cancel", {}, follow=True).status == 403
        assert post(op, event_path + "?handler=Correct", {"attendanceId": str(attendance_id), "restore": "false", "reason": "Forbidden"}, follow=True).status == 403
        assert query("SELECT cancelled_at FROM pillar.event_occurrences WHERE id=%s", (event,), True)[0] is None
        op_id = query("SELECT a.id FROM pillar.app_users a JOIN pillar_auth.\"AspNetUsers\" u ON u.\"Id\"::text=a.identity_subject WHERE u.\"Email\"=%s", (operator_email,), True)[0]
        success(post(page, "/Staff?handler=Access", {"userId": str(op_id), "role": "attendance_operator", "disabled": "true"}))
        response = op.request.get(BASE + event_path, max_redirects=0)
        assert response.status == 302 and "/Account/Login" in response.headers.get("location", "")
        print("PASS role restrictions and immediate disabled-account enforcement", flush=True)

        success(post(page, "/Staff?handler=Access", {"userId": str(op_id), "role": "attendance_operator", "disabled": "false"}))
        sign_in(op, operator_email, operator_password, operator_secret)
        op.goto(BASE + "/Account/Password")
        new_password = "Changed!" + secrets.token_urlsafe(24)
        op.get_by_label("Current password", exact=True).fill(operator_password)
        op.get_by_label("New password", exact=True).fill(new_password)
        op.get_by_label("Confirm new password", exact=True).fill(new_password)
        op.get_by_role("button", name="Update password").click()
        assert "/Account/Login" in op.url
        sign_in(op, operator_email, new_password, operator_secret)
        print("PASS password change and subsequent MFA login", flush=True)

        page.goto(BASE + event_path)
        query("UPDATE pillar.event_occurrences SET check_in_closes_at=now()-interval '1 minute' WHERE id=%s RETURNING id", (event,))
        success(post(page, event_path + "?handler=Manual", {"memberCode": father[1], "reason": "Outside window"}))
        assert query("SELECT count(*) FROM pillar.attendance_records WHERE event_occurrence_id=%s", (event,), True)[0] == 2
        query("UPDATE pillar.event_occurrences SET check_in_closes_at=now()+interval '1 hour' WHERE id=%s RETURNING id", (event,))
        success(post(page, event_path + "?handler=Cancel", {}))
        success(post(page, event_path + "?handler=Manual", {"memberCode": father[1], "reason": "Closed event"}))
        assert query("SELECT count(*) FROM pillar.attendance_records WHERE event_occurrence_id=%s", (event,), True)[0] == 2
        page.goto(BASE + "/")
        page.screenshot(path=str(ARTIFACTS / "dashboard.png"), full_page=True)
        page.goto(BASE + detail)
        page.screenshot(path=str(ARTIFACTS / "member-family-history.png"), full_page=True)
        page.set_viewport_size({"width": 390, "height": 844})
        page.goto(BASE + "/Members")
        assert page.evaluate("document.documentElement.scrollWidth <= window.innerWidth + 1")
        page.screenshot(path=str(ARTIFACTS / "mobile-members.png"), full_page=True)
        assert not errors, "Browser JavaScript error detected"
        print("PASS closed/cancelled event rejection, desktop/mobile rendering, and no JavaScript errors", flush=True)
        browser.close()
    print("All browser integration checks passed.", flush=True)


if __name__ == "__main__":
    main()
