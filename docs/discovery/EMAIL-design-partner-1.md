# Design partner #1: invitation e-mail (draft, English)

Send from your own address. Replace [brackets]. Check the facts in the first paragraph against the customer's thread
before sending.

---

**Subject:** Early access to what your 404 triage idea turned into (and a 20-minute chat?)

Hi [Name],

A while ago you asked for a way to sort the 404 log, and then helped me find the bulk-apply bug. Both shipped (404
categories in 1.10, the bulk fix in 1.10.2), and your use case shaped how they work. Thank you.

I have been building the next layer and would like you to try it first, free, as a design partner.
**Redirect Manager Premium (alpha)** adds two things to the 404 log:

- **Search Console traffic per 404.** The log shows how many Google clicks and impressions each broken URL used to get,
  sorted so the expensive ones come first. Search Console is read directly by your site; nothing passes through me.
- **Smart target suggestions.** For a 404, it ranks your own pages as redirect targets, using the old URL's words and
  the Google queries that used to reach it. You still review and save every redirect; nothing changes by itself.

**What I'd ask in return:** a 20-minute call (or a short written reply) about how you deal with redirects today, and
honest feedback while you try it. If it turns out useful and you are happy to, a sentence I may quote. No obligation
either way.

**What you need:**
- Umbraco 17 or later (the alpha does not support 13)
- A GitHub account, so I can give you access to a private package feed
- Access to the site's Search Console property, to add a read-only service account (I'll send step-by-step
  instructions; it takes about 10 minutes)

**Good to know:**
- The free Redirect Manager stays free. Everything that exists today keeps working without a license, forever; Premium
  only adds new things. The promise is written down here:
  https://github.com/batuhanilgarr/Umbraco-RedirectManager/blob/main/docs/FREE-FOREVER.md
- If the Premium license is missing or expires, only the Premium extras pause. Your redirects are never affected.
- Telemetry stays off unless you turn it on.

If you're up for it, reply with your **GitHub username**, your **Umbraco version** and the **site domain(s)**, and a
couple of times that suit you for the call. I'll send the license key and the install guide the same day.

Thanks again,
Batuhan
batuhannilgarr@gmail.com

---

## Checklist when they say yes
1. Invite their GitHub username as **Read** on the package `BT.RedirectManager.Premium` (see Premium repo `docs/OWNER-RELEASE.md`).
2. Issue a license (`RmLicense issue ... --register`), `--max-version` = latest Premium version.
3. Send `docs/ALPHA-INSTALL.md` and the key. Add them to `TRACKER.csv` (source: design partner #1).
4. Book the call; use `INTERVIEW-GUIDE.md`.
