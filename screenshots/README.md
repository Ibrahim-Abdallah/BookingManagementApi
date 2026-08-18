# Genuine Screenshot Capture Checklist

Capture these manually from the running API after automated verification. Do not create mock or synthetic PNGs. Crop irrelevant browser chrome if useful and hide passwords, tokens, authorization headers, connection strings, and other secrets.

- `01-scalar-overview.png`: Scalar overview with useful endpoint groups and no token displayed.
- `02-authentication.png`: successful login or authenticated Scalar state; mask tokens and show no real password.
- `03-availability.png`: real `GET /api/availability` with multiple or clearly valid slots.
- `04-reservation-hold.png`: real 201 showing reservation/reference, Held status, and expiry; hide token.
- `05-reservation-lifecycle.png`: clearest genuine confirmed, rescheduled, or cancelled response.
- `06-background-expiration.png`: persisted Expired reservation after cleanup, including expiry/update times where practical.
- `07-reporting-summary.png`: Admin-only real report with counts and top services/resources.

Before capture, verify Scalar summaries/Bearer auth, validation and real 409 Problem Details, availability, hold/confirmation, reschedule/cancel, background expiration, reporting, and README setup commands.
