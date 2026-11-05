# ADR-0013 — Web push for plan reminders: opt-in, one generic notification a day

- **Status:** Accepted
- **Date:** 2026-11-05
- **Owners:** Repository owner; Claude Code (implementation)
- **Decision scope:** Outbound notifications, browser subscriptions, background delivery, outbound URL handling
- **Related:** [PLAN-0062](../../plans/PLAN-0062-guided-shopping-planning.md) Slice 7, [PLAN-0048](../../plan-status.md), [ADR-0012](0012-offline-shopping-list.md)
- **Supersedes:** None
- **Superseded by:** None

## Context

Reminders such as "thaw today" exist only inside the app, so they help only someone who opens it. The owner chose opt-in notifications outside the app in the PLAN-0062 discovery. Notifications appear on lock screens, including shared or visible ones, and people's food and restrictions are sensitive. The browser supplies the address the server calls, so subscribing also creates an outbound request controlled by the client.

## Decision

1. **Standard Web Push with VAPID.** `Lib.Net.Http.WebPush` (exact pin) signs and encrypts. The keys come from `Planning__WebPush__PublicKey`, `__PrivateKey`, and `__Subject`, and are never committed. Without valid keys, the API reports the feature as unavailable, the interface hides it, and the background job does nothing.
2. **Opt-in only.** Permission is requested only after a tap, from the profile page or from the prompt on an accepted menu. It is never requested on page load, and "Agora não" is remembered on that browser.
3. **The payload carries no user data.** The server sends only `{ "type": "plan_reminder", "language": "<code>" }`. The worker's script `push-sw.js` holds the sentence in pt-BR, en, and es, and always opens `/app/hoje`. No URL or text from the payload reaches the screen, so the backend still returns codes rather than sentences.
4. **At most one notification per owner per day.** A job runs at the top of each hour and handles owners whose profile time zone says it is between 09:00 and 09:59. An owner without a time zone gets no notifications. It reads the same derived reminders as "Prepare-se" (`buy`, `thaw`, and `cook_by` once that kind exists) due that day.
5. **Claim before sending.** `planning.push_delivery_days` records the owner's last day, using a conditional upsert before the first send. A crash between the claim and the send loses that day's notification, and a restart can never send it twice. The plan requires no repeat after a restart, and this is the simplest way to guarantee it.
6. **Closed list of push services.** A subscription is accepted only for HTTPS on the default port, with no user information, on a host of Firebase, Mozilla, Windows, or Apple push. The sender checks again before calling, does not follow redirects, and has a 15-second limit. Keys are checked for the shape the standard requires. This keeps the endpoint from reaching internal addresses.
7. **Subscriptions end when they stop working or when the person leaves.** HTTP 404 or 410 from the push service deletes the row. Each owner keeps at most 10, and the oldest goes first. A logout removes the browser's subscription on a best-effort basis, within 2 seconds, so the next person on a shared device does not get them. An endpoint subscribed by a second owner moves to that owner.
8. **The job has no signed-in user.** It reads through ports with an explicit owner id, in one service scope per owner. One failing subscription or owner does not stop the others. Logs and metrics carry outcome and exception type only, never endpoints or keys.

## Alternatives considered

- **Sentences in the backend:** rejected. It would break the rule that the backend returns codes (PLAN-0062 invariant 11).
- **Naming the food in the notification:** rejected by the plan. It would be more useful, and it would show what someone eats and restricts on a lock screen.
- **Any HTTPS endpoint:** rejected. It would let a signed-in client make the server call arbitrary hosts.
- **Writing VAPID and message encryption directly:** rejected. The library is small and MIT licensed, and this code is easy to get subtly wrong. The vulnerability gate covers it.
- **Sending after a late restart:** rejected. A 09:00 hour missed because the service was down stays missed, because a notification in the afternoon says little that "Hoje" does not.

## Consequences

- People on iPhone receive notifications only from the installed app. The profile page says so.
- Rolling back to a release without `push-sw.js` leaves subscriptions in place with no handler. A browser may then show its own generic message until the worker is removed, so disable the keys first if rolling back for long.
- `/push-sw.js` can stay at the CDN for a while after a deploy, because only `/sw.js` is marked for revalidation. Its copy rarely changes, and a stale copy still shows a correct notification.
- Subscriptions are personal data: they appear in the privacy inventory, and export and deletion must include them when PLAN-0049 is delivered.
- Delivery depends on third-party push services. The payload is encrypted end to end and contains nothing about the person, and those services see only that a notification was sent and when.
- The owner must generate keys with `scripts/ops/generate-vapid-keys.mjs` before the feature appears.
