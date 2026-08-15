# Booking Management API — Project Plan

## 1. Project Purpose

Build a portfolio-ready **ASP.NET Core REST API for reservation and booking management** that demonstrates practical backend engineering skills beyond basic CRUD.

The project should showcase:

- REST API design
- Authentication and authorization
- JWT access tokens
- Refresh-token rotation and revocation
- Role-based authorization
- Entity Framework Core
- SQL Server
- Real-world scheduling rules
- Service and resource management
- Weekly availability schedules
- Blocked/unavailable periods
- Availability calculation
- Temporary booking holds
- Reservation confirmation
- Cancellation and rescheduling
- Concurrency-safe booking
- Double-booking prevention
- Database transactions
- Background services
- Time-based business rules
- Pagination, filtering, and sorting
- Validation
- Centralized error handling
- Dapper for reporting
- Automated unit and integration tests
- OpenAPI / Scalar documentation
- Clean, maintainable code
- Professional GitHub portfolio presentation

The application domain will be a **generic Booking Management API** suitable for businesses such as:

- Clinics
- Salons
- Consultants
- Training centers
- Meeting rooms
- Equipment rental by time slot
- Other appointment-based businesses

The goal is **not** to build a complete SaaS platform or a full hospital/restaurant management system.

The project must remain focused on the backend scheduling and reservation problems that are valuable in real freelance work.

The primary technical differentiator is:

```text
Availability calculation
+
Temporary booking holds
+
Concurrency-safe double-booking prevention
+
Reservation lifecycle management
```

---

# 2. Project Success Criteria

The finished repository should immediately communicate that the developer can build more than CRUD APIs.

A reviewer should be able to identify these capabilities quickly:

```text
Secure authentication
Business-rule-heavy scheduling
SQL Server concurrency control
Transactional booking
Background processing
Clean API design
Automated testing
Professional documentation
```

The repository should remain small enough to understand during a client review.

Avoid unnecessary architecture ceremony.

---

# 3. Technology Stack

Use:

- .NET 10
- ASP.NET Core 10 Web API
- C#
- Entity Framework Core 10
- SQL Server
- JWT Bearer Authentication
- ASP.NET Core built-in OpenAPI
- Scalar interactive API documentation
- FluentValidation
- Dapper for reporting
- xUnit
- ASP.NET Core integration testing

Use framework-native functionality where practical.

For background processing use:

```text
BackgroundService
PeriodicTimer
TimeProvider
```

Do not introduce Hangfire, Quartz, RabbitMQ, Redis, or other infrastructure unless a concrete need appears later.

---

# 4. Repository Structure

Repository name:

```text
BookingManagementApi
```

Target structure:

```text
BookingManagementApi/
├── src/
│   └── BookingManagementApi/
├── tests/
│   └── BookingManagementApi.Tests/
├── screenshots/
├── README.md
├── PROJECT_PLAN.md
├── .gitignore
├── global.json
└── BookingManagementApi.slnx
```

Keep the solution intentionally simple.

Do not split the application into multiple class-library projects unless there is a concrete reason.

---

# 5. Application Areas

The application contains eight main areas:

1. Authentication
2. Services
3. Resources
4. Resource Availability
5. Availability Search
6. Reservations
7. Background Processing
8. Administration and Reporting

---

# 6. Roles

Implement two roles:

```text
Customer
Admin
```

Newly registered accounts receive:

```text
Customer
```

Admin accounts should be created only through safe development configuration or database administration.

Do not allow public registration to choose the Admin role.

---

# 7. Time and Scheduling Model

Scheduling is one of the project's most important areas.

Use the following rules:

- Persist reservation timestamps in UTC.
- Use `DateTimeOffset` for persisted timestamps where practical.
- Use `TimeOnly` for recurring weekly availability rules.
- Interpret weekly availability in one configurable business time zone.
- Never rely on the web server's local time.
- Use `TimeProvider` so time-dependent logic can be tested.
- Calculate reservation end time server-side from the selected service duration.
- The client must not provide a trusted `EndAtUtc`.

Suggested scheduling configuration:

```json
{
  "Scheduling": {
    "BusinessTimeZoneId": "Egypt Standard Time",
    "SlotIntervalMinutes": 15,
    "HoldDurationMinutes": 5,
    "MinimumAdvanceMinutes": 30,
    "MaximumBookingHorizonDays": 90,
    "MinimumCancellationNoticeMinutes": 60
  }
}
```

Configuration values are examples and may be adjusted.

All service durations must be compatible with the configured slot interval.

For example, with a 15-minute slot interval:

```text
15 minutes   ✅
30 minutes   ✅
45 minutes   ✅
60 minutes   ✅
50 minutes   ❌
```

This keeps availability generation deterministic and understandable.

---

# 8. Core Entities

Core entities:

```text
User
RefreshToken
Service
Resource
ResourceService
AvailabilityRule
BlockedPeriod
Reservation
```

Suggested supporting enums:

```text
ReservationStatus
DayOfWeek / System.DayOfWeek
```

Reservation statuses:

```text
Held
Confirmed
Cancelled
Completed
NoShow
Expired
```

`Held` represents a short-lived reservation hold before final confirmation.

---

# 9. Authentication

Implement:

```http
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh-token
POST /api/auth/logout
```

## Registration

Customer registration fields:

```text
FirstName
LastName
Email
Password
ConfirmPassword
```

Requirements:

- Normalize email.
- Email must be unique.
- Hash passwords using ASP.NET Core password hashing.
- Never persist plaintext passwords.
- Never expose password hashes.
- Assign the `Customer` role automatically.

---

# 10. JWT Authentication

JWT access tokens should contain only useful claims such as:

```text
UserId
Email
Role
Jti
```

Requirements:

- Validate issuer.
- Validate audience.
- Validate signing key.
- Validate token lifetime.
- Use UTC timestamps.
- Keep signing secrets outside source control.
- Use User Secrets or environment variables locally.

Suggested configuration:

```json
{
  "Jwt": {
    "Issuer": "BookingManagementApi",
    "Audience": "BookingManagementApi.Client",
    "Key": "",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  }
}
```

---

# 11. Refresh Tokens

Entity:

```text
RefreshToken
- Id
- TokenHash
- UserId
- CreatedAtUtc
- ExpiresAtUtc
- RevokedAtUtc
- ReplacedByTokenId
```

Requirements:

- Generate cryptographically secure random tokens.
- Persist hashes only.
- Support rotation.
- Support revocation.
- Reject expired tokens.
- Reject revoked tokens.
- Reject replay of previously rotated tokens.
- Logout revokes the submitted active refresh token.
- Never log raw access or refresh tokens.

---

# 12. Services

A `Service` describes something that a customer can book.

Entity:

```text
Service
- Id
- Name
- Description
- DurationMinutes
- IsActive
- CreatedAtUtc
- UpdatedAtUtc
```

Admin endpoints:

```http
GET    /api/admin/services
GET    /api/admin/services/{id}
POST   /api/admin/services
PUT    /api/admin/services/{id}
PATCH  /api/admin/services/{id}/activation
```

Customer/public authenticated endpoint:

```http
GET /api/services
GET /api/services/{id}
```

Rules:

- Service name is required.
- Duration must be greater than zero.
- Duration must be compatible with the configured slot interval.
- Inactive services cannot be newly booked.
- Historical reservations must remain readable if a service is later deactivated.
- Do not hard-delete a service if reservation history depends on it.

---

# 13. Resources

A `Resource` is the bookable provider/object.

Examples:

```text
Doctor
Consultant
Stylist
Room
Desk
Machine
Court
```

Entity:

```text
Resource
- Id
- Name
- Description
- IsActive
- CreatedAtUtc
- UpdatedAtUtc
```

Admin endpoints:

```http
GET    /api/admin/resources
GET    /api/admin/resources/{id}
POST   /api/admin/resources
PUT    /api/admin/resources/{id}
PATCH  /api/admin/resources/{id}/activation
```

Customer/public authenticated endpoints:

```http
GET /api/resources
GET /api/resources/{id}
```

Rules:

- Inactive resources cannot accept new reservations.
- Historical reservations remain available.
- Prefer deactivation over destructive deletion.

---

# 14. Resource-Service Assignment

Not every resource must support every service.

Join entity:

```text
ResourceService
- ResourceId
- ServiceId
```

Admin endpoints:

```http
POST   /api/admin/resources/{resourceId}/services/{serviceId}
DELETE /api/admin/resources/{resourceId}/services/{serviceId}
GET    /api/admin/resources/{resourceId}/services
```

Database constraint:

```text
UNIQUE(ResourceId, ServiceId)
```

Rules:

- Both resource and service must exist.
- A reservation may only be created when the selected resource supports the selected service.
- Duplicate assignments must be rejected safely.

---

# 15. Weekly Availability Rules

Entity:

```text
AvailabilityRule
- Id
- ResourceId
- DayOfWeek
- StartTime
- EndTime
- IsActive
- CreatedAtUtc
- UpdatedAtUtc
```

Example:

```text
Resource: Room 1
Monday
09:00 → 17:00
```

Admin endpoints:

```http
GET    /api/admin/resources/{resourceId}/availability-rules
POST   /api/admin/resources/{resourceId}/availability-rules
PUT    /api/admin/availability-rules/{id}
DELETE /api/admin/availability-rules/{id}
```

Rules:

- `StartTime < EndTime`.
- Rules must not overlap for the same resource and day.
- Availability rules use the configured business time zone.
- Multiple non-overlapping shifts per day are allowed.

Example:

```text
09:00 → 12:00
13:00 → 17:00
```

This supports lunch breaks without special logic.

---

# 16. Blocked Periods

Blocked periods temporarily make a resource unavailable.

Examples:

```text
Holiday
Vacation
Maintenance
Internal meeting
Emergency closure
```

Entity:

```text
BlockedPeriod
- Id
- ResourceId
- StartsAtUtc
- EndsAtUtc
- Reason
- CreatedAtUtc
```

Admin endpoints:

```http
GET    /api/admin/resources/{resourceId}/blocked-periods
POST   /api/admin/resources/{resourceId}/blocked-periods
PUT    /api/admin/blocked-periods/{id}
DELETE /api/admin/blocked-periods/{id}
```

Rules:

- `StartsAtUtc < EndsAtUtc`.
- A blocked period may cover part of a day or several days.
- New holds/reservations cannot overlap a blocked period.
- Existing confirmed reservations should prevent creation of a conflicting blocked period unless an explicit safe administrative workflow is later added.

For Version 1:

```text
Conflicting block creation → 409 Conflict
```

Do not silently cancel customer reservations.

---

# 17. Availability Search

Customer endpoint:

```http
GET /api/availability
```

Suggested query parameters:

```text
serviceId
date
resourceId (optional)
```

Example:

```http
GET /api/availability?serviceId=3&date=2026-08-20
```

If `resourceId` is omitted, return available slots across all active resources that support the service.

Suggested response:

```json
{
  "date": "2026-08-20",
  "businessTimeZoneId": "Egypt Standard Time",
  "serviceId": 3,
  "slots": [
    {
      "resourceId": 7,
      "resourceName": "Room 2",
      "startsAtUtc": "2026-08-20T07:00:00Z",
      "endsAtUtc": "2026-08-20T07:30:00Z"
    }
  ]
}
```

Availability calculation should consider:

```text
Weekly availability rules
        ↓
Service duration
        ↓
Slot interval
        ↓
Blocked periods
        ↓
Confirmed reservations
        ↓
Unexpired Held reservations
        ↓
Minimum advance time
        ↓
Maximum booking horizon
```

Important:

> Availability search is advisory.

A slot may become unavailable immediately after being returned.

Only successful hold creation guarantees temporary ownership of the slot.

This distinction must be documented clearly.

---

# 18. Reservation Entity

Entity:

```text
Reservation
- Id
- ReferenceNumber
- UserId
- ResourceId
- ServiceId
- StartsAtUtc
- EndsAtUtc
- Status
- HoldExpiresAtUtc
- CreatedAtUtc
- UpdatedAtUtc
- ConfirmedAtUtc
- CancelledAtUtc
- CancellationReason
```

Optional optimistic concurrency property:

```text
RowVersion
```

Historical snapshot fields should be considered for values whose later changes should not alter historical reservation display.

Recommended snapshots:

```text
ServiceNameSnapshot
ResourceNameSnapshot
ServiceDurationMinutesSnapshot
```

This demonstrates deliberate historical-data design.

---

# 19. Create Reservation Hold

Customer endpoint:

```http
POST /api/reservation-holds
```

Suggested request:

```json
{
  "serviceId": 3,
  "resourceId": 7,
  "startsAtUtc": "2026-08-20T07:00:00Z"
}
```

The client must NOT send:

```text
UserId
EndsAtUtc
HoldExpiresAtUtc
Status
ServiceDuration
```

The server determines these values.

Workflow:

```text
Authenticated customer
        ↓
Validate service
        ↓
Validate resource
        ↓
Validate resource supports service
        ↓
Calculate end time from service duration
        ↓
Validate booking horizon / lead time
        ↓
Validate weekly availability
        ↓
Validate blocked periods
        ↓
Acquire resource booking lock
        ↓
Check overlapping active reservations
        ↓
Create Held reservation
        ↓
Set hold expiration
        ↓
Commit transaction
```

Suggested response:

```json
{
  "reservationId": 123,
  "referenceNumber": "BKG-20260820-ABC123",
  "status": "Held",
  "startsAtUtc": "2026-08-20T07:00:00Z",
  "endsAtUtc": "2026-08-20T07:30:00Z",
  "holdExpiresAtUtc": "2026-08-20T06:35:00Z"
}
```

---

# 20. Double-Booking Prevention

This is the project's most important technical feature.

Example:

```text
Resource 7
Available slot: 10:00 → 10:30

Customer A creates hold at the same moment
Customer B creates hold at the same moment
```

Only one request may succeed.

The other request must receive:

```text
409 Conflict
```

Do not rely on only:

```text
Check availability
if available:
    insert reservation
```

because two concurrent requests can both observe the same availability.

## Required SQL Server Approach

Use a short database transaction.

Inside the transaction:

1. Acquire an update lock for the selected resource.
2. Re-run all conflict checks that matter.
3. Insert the Held reservation.
4. Commit.

A suitable SQL Server locking pattern may use:

```sql
SELECT Id
FROM Resources WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @ResourceId;
```

The exact implementation may use parameterized EF Core raw SQL or another safe approach.

After the resource row lock is acquired, perform the overlap query before insert.

Overlap condition:

```text
Existing.StartsAtUtc < Requested.EndsAtUtc
AND
Existing.EndsAtUtc > Requested.StartsAtUtc
```

Blocking statuses:

```text
Confirmed
Held when HoldExpiresAtUtc > current UTC time
```

Non-blocking statuses:

```text
Cancelled
Completed
NoShow
Expired
Held after expiration
```

Why lock by resource:

- Concurrent booking attempts for the same resource are serialized.
- Different resources may still be booked concurrently.
- The implementation remains understandable.
- No distributed lock infrastructure is required.

Keep the transaction short.

Do not call external APIs inside the booking transaction.

Add an integration test that sends concurrent hold requests for the same resource/time.

Expected result:

```text
Exactly 1 success
Exactly 1 conflict
No overlapping active reservation rows
```

Use SQL Server for this test.

Do not use EF Core InMemory provider for concurrency correctness tests.

---

# 21. Confirm Reservation

Customer endpoint:

```http
POST /api/reservations/{id}/confirm
```

Rules:

- Reservation must belong to the authenticated customer.
- Status must be `Held`.
- Hold must not be expired.
- Confirmation changes status to `Confirmed`.
- Set `ConfirmedAtUtc`.
- Clear or retain `HoldExpiresAtUtc` consistently according to implementation choice.
- Confirmation must be idempotent-safe.

Suggested behavior:

```text
Held + not expired → Confirmed
Held + expired     → 409 Conflict
Confirmed again    → 200/409 according to documented idempotency rule
Other status       → 409 Conflict
```

Prefer a predictable documented rule.

---

# 22. Customer Reservation Endpoints

Authenticated Customer:

```http
GET  /api/reservations
GET  /api/reservations/{id}
POST /api/reservations/{id}/confirm
POST /api/reservations/{id}/cancel
POST /api/reservations/{id}/reschedule
```

Customers may access only their own reservations.

Do not accept a customer ID from query or route parameters to determine ownership.

Derive ownership from JWT claims.

Cross-user reservation access should return an access-safe response such as:

```text
404 Not Found
```

---

# 23. Reservation Listing

`GET /api/reservations` should support:

```text
pageNumber
pageSize
status
fromDate
toDate
```

Default sorting:

```text
StartsAtUtc DESC
Id DESC
```

Keep sorting deliberately limited.

Do not add a generic dynamic sorting framework.

---

# 24. Cancellation

Customer endpoint:

```http
POST /api/reservations/{id}/cancel
```

Optional request:

```json
{
  "reason": "Schedule changed"
}
```

Rules:

- Reservation must belong to the authenticated customer.
- `Held` reservations may be cancelled.
- `Confirmed` reservations may be cancelled only before the configured cancellation cutoff.
- `Completed`, `NoShow`, `Cancelled`, and `Expired` reservations cannot be cancelled.
- Cancellation must not accidentally make another reservation disappear.
- Repeated cancellation attempts must not cause duplicate side effects.

Suggested transition:

```text
Held      → Cancelled
Confirmed → Cancelled
```

Invalid transitions return:

```text
409 Conflict
```

---

# 25. Rescheduling

Customer endpoint:

```http
POST /api/reservations/{id}/reschedule
```

Version 1 keeps rescheduling intentionally bounded.

Suggested request:

```json
{
  "startsAtUtc": "2026-08-22T09:00:00Z"
}
```

Rules:

- Reservation must belong to the authenticated customer.
- Only a `Confirmed` reservation may be rescheduled.
- Version 1 reschedules on the **same resource and same service**.
- New start time must satisfy all normal availability rules.
- End time is recalculated server-side.
- The concurrency-safe resource lock must be used.
- Conflict checks must exclude the reservation being rescheduled.
- Reschedule must execute transactionally.
- If validation fails, the original reservation remains unchanged.

Keeping the same resource/service avoids unnecessary multi-resource locking complexity in Version 1.

Changing resource/service may be added later as a new workflow.

---

# 26. Reservation Status Transitions

Centralize valid status transitions.

Suggested customer/system transitions:

```text
Held      → Confirmed
Held      → Cancelled
Held      → Expired

Confirmed → Cancelled
Confirmed → Completed
Confirmed → NoShow
```

Terminal statuses:

```text
Cancelled
Completed
NoShow
Expired
```

Invalid examples:

```text
Expired   → Confirmed   ❌
Cancelled → Confirmed   ❌
Completed → Held        ❌
NoShow    → Confirmed   ❌
```

Do not scatter transition logic across controllers.

---

# 27. Administration — Reservations

Admin endpoints:

```http
GET   /api/admin/reservations
GET   /api/admin/reservations/{id}
PATCH /api/admin/reservations/{id}/status
```

Suggested Admin filters:

```text
pageNumber
pageSize
status
customerEmail
resourceId
serviceId
fromDate
toDate
```

Admin may perform business-valid operational transitions such as:

```text
Confirmed → Completed
Confirmed → NoShow
```

Version 1 should not allow Admin to bypass double-booking rules by arbitrarily changing date/resource through a generic update endpoint.

Avoid:

```http
PUT /api/admin/reservations/{id}
```

for unrestricted reservation editing.

Use explicit use-case endpoints instead.

---

# 28. Background Hold Expiration

Implement a hosted background service:

```text
ExpiredReservationHoldService
```

Purpose:

```text
Find Held reservations
where HoldExpiresAtUtc <= current UTC time
and mark them Expired
```

Recommended implementation:

- Derive from `BackgroundService`.
- Use `PeriodicTimer`.
- Create a DI scope for each execution cycle.
- Use `TimeProvider`.
- Process in bounded batches.
- Use async database APIs.
- Pass cancellation tokens.
- Log only useful summary information.

The job must be idempotent.

Example:

```text
Held + expired → Expired
Expired        → no change
Confirmed      → no change
Cancelled      → no change
```

Important correctness rule:

The booking conflict query must already treat an expired hold as non-blocking even if the background job has not cleaned it yet.

Therefore:

```text
System correctness must NOT depend on exact background-job timing.
```

The background service performs cleanup/state maintenance, not correctness-critical locking.

This is an important portfolio design point.

---

# 29. Availability and Booking Configuration

Create strongly typed options:

```text
SchedulingOptions
```

Suggested properties:

```text
BusinessTimeZoneId
SlotIntervalMinutes
HoldDurationMinutes
MinimumAdvanceMinutes
MaximumBookingHorizonDays
MinimumCancellationNoticeMinutes
HoldCleanupIntervalSeconds
```

Validate configuration at application startup.

Reject invalid configuration such as:

```text
SlotIntervalMinutes <= 0
HoldDurationMinutes <= 0
MaximumBookingHorizonDays <= 0
```

---

# 30. Reporting

Add a small Admin-only reporting area using **Dapper**.

Do not rebuild the normal persistence layer with Dapper.

Recommended endpoint:

```http
GET /api/admin/reports/reservations-summary
```

Suggested filters:

```text
fromDate
toDate
```

Suggested response:

```json
{
  "totalReservations": 240,
  "confirmed": 110,
  "completed": 85,
  "cancelled": 30,
  "noShow": 15,
  "topServices": [
    {
      "serviceId": 3,
      "serviceName": "Consultation",
      "reservationCount": 70
    }
  ],
  "topResources": [
    {
      "resourceId": 7,
      "resourceName": "Room 2",
      "reservationCount": 54
    }
  ]
}
```

Define clearly which statuses contribute to each metric.

Keep reporting intentionally small and readable.

Use parameterized Dapper queries.

---

# 31. Entity Relationships

Core relationships:

```text
User
 ├── RefreshTokens
 └── Reservations
         ├── Service
         └── Resource

Service
 └── ResourceServices
         └── Resource

Resource
 ├── ResourceServices
 ├── AvailabilityRules
 ├── BlockedPeriods
 └── Reservations
```

Suggested cardinalities:

```text
User 1 ---- * RefreshToken
User 1 ---- * Reservation

Service 1 ---- * Reservation
Resource 1 ---- * Reservation

Service 1 ---- * ResourceService
Resource 1 ---- * ResourceService

Resource 1 ---- * AvailabilityRule
Resource 1 ---- * BlockedPeriod
```

---

# 32. Database Constraints and Indexes

Add appropriate:

- Primary keys
- Foreign keys
- Unique indexes
- Required fields
- Length limits
- Check constraints where useful
- Query indexes
- Deliberate delete behavior

Important unique constraints:

```text
User.NormalizedEmail
ResourceService(ResourceId, ServiceId)
Reservation.ReferenceNumber
```

Useful indexes:

```text
Reservation(UserId, StartsAtUtc)
Reservation(ResourceId, StartsAtUtc, EndsAtUtc)
Reservation(Status, HoldExpiresAtUtc)
AvailabilityRule(ResourceId, DayOfWeek)
BlockedPeriod(ResourceId, StartsAtUtc, EndsAtUtc)
```

Consider filtered or included indexes only if they clearly improve the real queries.

Do not add indexes merely for decoration.

Historical reservations must not be destroyed by cascading deletes.

Prefer:

```text
Restrict
NoAction
```

for Service/Resource relationships that preserve reservation history.

---

# 33. Architecture

Recommended application structure:

```text
src/BookingManagementApi/
├── BackgroundServices/
├── Common/
│   ├── Errors/
│   ├── Pagination/
│   └── Time/
├── Configuration/
├── Controllers/
│   ├── AuthController.cs
│   ├── AvailabilityController.cs
│   ├── ReservationHoldsController.cs
│   ├── ReservationsController.cs
│   └── Admin/
│       ├── ServicesController.cs
│       ├── ResourcesController.cs
│       ├── AvailabilityRulesController.cs
│       ├── BlockedPeriodsController.cs
│       ├── ReservationsController.cs
│       └── ReportsController.cs
├── Data/
│   ├── AppDbContext.cs
│   ├── Configurations/
│   └── Migrations/
├── DTOs/
│   ├── Auth/
│   ├── Services/
│   ├── Resources/
│   ├── Availability/
│   ├── Reservations/
│   ├── Admin/
│   └── Reports/
├── Entities/
├── Enums/
├── Interfaces/
├── Services/
├── Validation/
└── Program.cs
```

---

# 34. Architecture Rules

- Controllers remain thin.
- Services contain use-case and business logic.
- EF Core `AppDbContext` may be used directly by application services.
- Do not add a generic repository wrapper around EF Core.
- Dapper is limited primarily to reporting.
- Do not use CQRS/MediatR unless a concrete need appears.
- Avoid unnecessary inheritance.
- Avoid unnecessary interfaces for classes that have no substitution/testing need.
- Prefer readable code over architecture ceremony.
- Keep booking concurrency logic explicit and easy to audit.
- Do not hide important transaction behavior behind generic abstractions.

---

# 35. Suggested Services

Possible service classes:

```text
AuthService
TokenService
RefreshTokenService
ServiceCatalogService
ResourceService
AvailabilityService
ReservationService
ReservationConcurrencyService
ReservationStatusService
ReportingService
```

Naming may change during implementation.

Avoid creating a service class only to wrap one `DbContext` call.

---

# 36. DTO Rules

Never expose EF Core entities directly.

Use request and response DTOs.

Examples:

```text
RegisterRequest
LoginRequest
RefreshTokenRequest
AuthResponse

CreateServiceRequest
UpdateServiceRequest
ServiceResponse

CreateResourceRequest
UpdateResourceRequest
ResourceResponse

CreateAvailabilityRuleRequest
UpdateAvailabilityRuleRequest
AvailabilityRuleResponse

CreateBlockedPeriodRequest
UpdateBlockedPeriodRequest
BlockedPeriodResponse

AvailabilityQueryParameters
AvailabilityResponse
AvailabilitySlotResponse

CreateReservationHoldRequest
ReservationHoldResponse

ReservationResponse
ReservationDetailsResponse
ReservationQueryParameters

CancelReservationRequest
RescheduleReservationRequest
UpdateReservationStatusRequest

ReservationSummaryResponse
TopServiceResponse
TopResourceResponse
```

Never accept server-owned fields from the client when they can be calculated safely.

---

# 37. Validation

Use FluentValidation.

Validate at minimum:

## Authentication

- Required first name
- Required last name
- Valid email
- Password strength
- Password confirmation

## Service

- Required name
- Valid length
- Duration > 0
- Duration compatible with slot interval

## Resource

- Required name
- Valid length

## Availability Rule

- Valid day
- Start time before end time
- No overlapping schedule rule for same resource/day

## Blocked Period

- Start before end
- Resource exists
- No unsafe conflict with confirmed reservations

## Availability Query

- Valid service ID
- Valid date
- Date within booking horizon
- Optional resource exists

## Hold Creation

- Valid service/resource
- Start aligned to slot interval
- Start in future according to lead time
- Resource supports service
- Fits inside weekly availability
- Does not overlap blocked period
- Does not overlap active reservation/hold

## Query Parameters

- Page number >= 1
- Page size between 1 and 100
- Valid status
- Valid date range

## Rescheduling

- Reservation is Confirmed
- New time is valid
- New time differs meaningfully from old time
- All normal availability rules pass

---

# 38. Error Handling

Use centralized ASP.NET Core Problem Details.

Expected responses include:

```text
200 OK
201 Created
204 No Content
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
500 Internal Server Error
```

Examples:

```text
Invalid validation               → 400
Invalid credentials              → 401
Customer calling Admin endpoint  → 403
Missing/foreign reservation      → 404
Duplicate email                  → 409
Slot already booked              → 409
Expired hold confirmation        → 409
Invalid status transition        → 409
Unsafe blocked-period conflict   → 409
Unexpected exception             → 500
```

Production `500` responses must not expose:

- Stack traces
- Exception types
- SQL details
- Connection strings
- JWT secrets
- Internal file paths

Include trace/request identifier where useful.

---

# 39. Logging

Use:

```text
ILogger<T>
```

Useful events may include:

- Reservation hold created
- Reservation confirmed
- Reservation cancelled
- Reservation rescheduled
- Booking conflict
- Background expired-hold cleanup summary
- Admin status transition
- Unexpected exception

Never log:

- Passwords
- Password hashes
- JWT signing keys
- Raw access tokens
- Raw refresh tokens
- Refresh-token hashes
- Authorization headers

Avoid unnecessary customer personal data in logs.

Use structured logging properties where useful:

```text
ReservationId
ResourceId
ServiceId
Status
```

---

# 40. OpenAPI / Scalar

Use ASP.NET Core built-in OpenAPI document generation with Scalar.

Requirements:

- Bearer authentication scheme
- Protected endpoints show security requirements
- Useful endpoint summaries
- Request/response documentation
- Representative error responses
- Development-only interactive documentation

Primary demo flow:

```text
Register
   ↓
Login
   ↓
Authorize
   ↓
Browse Services
   ↓
Search Availability
   ↓
Create Reservation Hold
   ↓
Confirm Reservation
   ↓
View Reservation
   ↓
Reschedule or Cancel
```

Admin demo:

```text
Admin Login
   ↓
Create Service
   ↓
Create Resource
   ↓
Assign Service to Resource
   ↓
Configure Weekly Availability
   ↓
Add Blocked Period
   ↓
View Reservations
   ↓
Mark Reservation Completed
   ↓
View Reservation Report
```

---

# 41. Automated Testing

Use meaningful unit and integration tests.

Do not chase an arbitrary coverage percentage.

Test important behavior.

## Authentication

- Register succeeds
- Duplicate email rejected
- Login succeeds
- Invalid credentials rejected
- Refresh succeeds
- Rotated refresh token cannot be reused
- Revoked token rejected
- Logout works

## Authorization

- Anonymous protected endpoints rejected
- Customer cannot call Admin endpoints
- Admin endpoints work for Admin

## Services and Resources

- Admin can create service
- Invalid duration rejected
- Customer cannot create service
- Admin can create resource
- Customer cannot create resource
- Resource-service assignment works
- Duplicate assignment rejected
- Inactive service/resource cannot be booked

## Availability Rules

- Admin can create weekly rule
- Invalid time range rejected
- Overlapping weekly rule rejected
- Multiple non-overlapping shifts allowed

## Blocked Periods

- Valid block created
- Invalid interval rejected
- Availability excludes blocked periods
- Conflicting block with confirmed reservation rejected

## Availability Search

- Correct slots generated
- Service duration respected
- Slot interval respected
- Minimum advance time respected
- Booking horizon respected
- Existing confirmed reservation removes slot
- Unexpired hold removes slot
- Expired hold does not remove slot
- Inactive resource excluded
- Unsupported resource/service combination excluded

## Reservation Holds

- Valid hold succeeds
- End time calculated server-side
- User identity comes from JWT
- Hold expiration calculated server-side
- Invalid weekly time rejected
- Blocked period rejected
- Existing reservation conflict rejected

## Concurrency

Critical test:

```text
Two concurrent hold requests
Same resource
Same time
```

Verify:

```text
Exactly one succeeds
Exactly one returns conflict
Database contains only one blocking reservation
```

Also test:

```text
Different resources may be booked concurrently
```

Use real SQL Server behavior for concurrency tests.

## Confirmation

- Owner confirms valid hold
- Another customer cannot confirm hold
- Expired hold cannot be confirmed
- Invalid status transition rejected

## Cancellation

- Owner cancels Held reservation
- Owner cancels Confirmed reservation before cutoff
- Cancellation after cutoff rejected
- Another customer cannot cancel
- Repeated cancellation does not create invalid state

## Rescheduling

- Valid reschedule succeeds
- End time recalculated
- New slot conflict rejected
- Original reservation unchanged after failed reschedule
- Another customer cannot reschedule
- Reschedule concurrency prevents overlap

## Background Service

- Expired held reservation becomes Expired
- Non-expired hold remains Held
- Confirmed reservation is not expired
- Re-running cleanup is idempotent
- Booking correctness does not depend on cleanup having run

## Administration

- Admin lists reservations
- Filters work
- Admin marks Confirmed reservation Completed
- Admin marks Confirmed reservation NoShow
- Invalid transition rejected

## Reporting

- Reservation counts calculated correctly
- Status counts correct
- Date filters correct
- Top services correct
- Top resources correct

## Error Handling

- Validation returns Problem Details
- Unauthorized response correct
- Forbidden response correct
- Not-found ownership hiding works
- Booking conflict returns 409
- Unexpected exception does not leak internals

---

# 42. Security Checklist

Before declaring the project complete:

- [ ] Passwords are hashed
- [ ] JWT signing secret is not committed
- [ ] JWT issuer validated
- [ ] JWT audience validated
- [ ] JWT lifetime validated
- [ ] JWT signature validated
- [ ] Refresh tokens are cryptographically random
- [ ] Refresh tokens are stored hashed
- [ ] Refresh-token rotation implemented
- [ ] Revoked token replay rejected
- [ ] Customer identity comes from JWT claims
- [ ] Reservation ownership enforced
- [ ] Customer cannot assign another UserId
- [ ] Customer cannot assign reservation status directly
- [ ] Customer cannot assign EndAtUtc directly
- [ ] Customer cannot assign hold expiration directly
- [ ] Admin endpoints require Admin role
- [ ] Resource/service compatibility validated
- [ ] Double-booking protected inside a database transaction
- [ ] Resource lock acquired before final overlap check
- [ ] SQL/raw commands are parameterized
- [ ] Expired holds cannot be confirmed
- [ ] Cross-user reservation access is protected
- [ ] Sensitive values are not logged
- [ ] Production errors do not expose internals
- [ ] HTTPS redirection enabled

---

# 43. Scope Boundaries

Version 1 intentionally excludes:

```text
Frontend UI
Mobile application
Online payments
Stripe
PayPal
Deposits
Invoices
Email delivery
SMS delivery
WhatsApp integration
Push notifications
Calendar sync
Google Calendar integration
Microsoft Outlook integration
Recurring reservations
Group bookings
Waitlists
Dynamic pricing
Coupons
Membership plans
Multi-tenant SaaS
Multiple business locations
Per-resource time zones
Employee payroll
Medical records
Restaurant table-combination logic
Seat maps
File uploads
Microservices
RabbitMQ
Kafka
Redis
Distributed locks
Hangfire
Quartz
Event sourcing
CQRS
MediatR
Kubernetes
Elasticsearch
AI scheduling
```

Do not add these unless explicitly requested later.

The scope is intentionally focused on:

```text
Scheduling
Availability
Booking
Concurrency
Reservation lifecycle
```

---

# 44. Definition of Done

The project is complete only when:

- [ ] Solution builds successfully
- [ ] Database migrations work
- [ ] Registration works
- [ ] Login works
- [ ] JWT authentication works
- [ ] Refresh-token rotation works
- [ ] Logout works
- [ ] Customer role works
- [ ] Admin role works
- [ ] Service management works
- [ ] Resource management works
- [ ] Resource-service assignment works
- [ ] Weekly availability works
- [ ] Blocked periods work
- [ ] Availability search works
- [ ] Slot interval rules work
- [ ] Booking horizon rules work
- [ ] Reservation hold works
- [ ] Hold expiration works
- [ ] Reservation confirmation works
- [ ] Double-booking is prevented under concurrency
- [ ] Booking uses a database transaction
- [ ] Customer reservation ownership is enforced
- [ ] Cancellation works
- [ ] Cancellation cutoff works
- [ ] Rescheduling works
- [ ] Failed reschedule leaves original reservation unchanged
- [ ] Background hold-expiration service works
- [ ] Admin reservation management works
- [ ] Status transition rules work
- [ ] Dapper reporting works
- [ ] FluentValidation works
- [ ] Problem Details works
- [ ] OpenAPI/Scalar authentication works
- [ ] Automated tests pass
- [ ] Concurrency integration tests pass against SQL Server
- [ ] Secrets are not committed
- [ ] README is complete
- [ ] Genuine API screenshots are included
- [ ] Repository is ready for Freelancer/GitHub portfolio use

Before final completion run:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
git diff --check
```

All commands must succeed.

---

# 45. Implementation Phases

Implementation must proceed one phase at a time.

Each phase gets:

```text
Dedicated Git branch
Focused implementation
Automated tests
Build verification
Pull request
Merge into master
```

---

## Phase 1 — Foundation

Branch:

```text
phase/01-foundation
```

Implement:

- Solution/project structure
- Test project
- EF Core
- SQL Server
- Core entities
- Entity configurations
- Relationships
- Database constraints
- Important indexes
- Initial migration
- OpenAPI
- Scalar
- Strongly typed configuration
- `SchedulingOptions`
- `TimeProvider`
- Base test infrastructure

Core entities created in this phase:

```text
User
RefreshToken
Service
Resource
ResourceService
AvailabilityRule
BlockedPeriod
Reservation
```

Verify:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
```

---

## Phase 2 — Authentication

Branch:

```text
phase/02-authentication
```

Implement:

- Registration
- Email normalization
- Password hashing
- Login
- JWT generation
- JWT validation
- Customer role
- Admin role setup
- Current user service
- Bearer OpenAPI support
- Authentication/authorization tests

---

## Phase 3 — Refresh Tokens

Branch:

```text
phase/03-refresh-tokens
```

Implement:

- Secure token generation
- SHA-256 token persistence
- Refresh endpoint
- Rotation
- Revocation
- Replay rejection
- Logout
- Tests

---

## Phase 4 — Services & Resources

Branch:

```text
phase/04-services-resources
```

Implement:

- Service Admin CRUD
- Resource Admin CRUD
- Safe activation/deactivation
- Resource-service assignment
- Customer service listing
- Customer resource listing
- FluentValidation
- Authorization
- Database constraints
- Tests

---

## Phase 5 — Schedule Management

Branch:

```text
phase/05-schedule-management
```

Implement:

- Weekly availability rules
- Multiple shifts per day
- Overlap validation for weekly rules
- Blocked periods
- Blocked-period conflict validation
- Business time-zone handling
- Admin endpoints
- Tests

---

## Phase 6 — Availability Engine

Branch:

```text
phase/06-availability-engine
```

Implement:

- Availability endpoint
- Slot generation
- Service-duration calculation
- Slot-interval alignment
- Minimum advance rule
- Maximum booking horizon
- Weekly availability application
- Blocked-period exclusion
- Existing reservation exclusion
- Active-hold exclusion
- Expired-hold handling
- Optional resource filter
- Tests

Important documentation:

```text
Availability results are advisory.
Only successful hold creation guarantees the slot temporarily.
```

---

## Phase 7 — Reservation Holds & Concurrency

Branch:

```text
phase/07-booking-concurrency
```

This is the project's most important phase.

Implement:

- Reservation hold endpoint
- Server-calculated end time
- Server-calculated hold expiration
- Booking transaction
- SQL Server resource row lock
- Final overlap check inside transaction
- Active status conflict rules
- 409 conflict handling
- Reservation reference number
- Historical snapshots
- Ownership rules
- Concurrency integration tests
- Rollback tests

Required proof:

```text
Two simultaneous requests for the same resource/time
→ exactly one successful hold
→ exactly one conflict
```

Do not merge this phase until the concurrency test is reliable.

---

## Phase 8 — Reservation Lifecycle

Branch:

```text
phase/08-reservation-lifecycle
```

Implement:

- Customer reservation list
- Customer reservation details
- Ownership enforcement
- Confirm hold
- Expired-hold protection
- Cancellation
- Cancellation cutoff
- Rescheduling
- Transactional reschedule
- Conflict-safe reschedule
- Centralized status-transition rules
- Admin reservation listing/details
- Admin Completed/NoShow transitions
- Tests

---

## Phase 9 — Background Jobs & Reporting

Branch:

```text
phase/09-background-reporting
```

Implement:

### Background processing

- `ExpiredReservationHoldService`
- `BackgroundService`
- `PeriodicTimer`
- Scoped processing
- TimeProvider usage
- Bounded cleanup batches
- Idempotent expiration
- CancellationToken propagation
- Background-service tests

### Reporting

- Dapper connection access
- Reservation summary endpoint
- Status counts
- Top services
- Top resources
- Date filtering
- Parameterized SQL
- Reporting tests

Keep background processing and reporting focused.

Do not introduce external infrastructure.

---

## Phase 10 — API Quality & Portfolio Polish

Branch:

```text
phase/10-portfolio-polish
```

Implement/refine:

- FluentValidation consistency
- Problem Details
- Centralized exception handling
- 409 conflict consistency
- Logging audit
- CancellationToken propagation
- OpenAPI summaries/descriptions
- Security review
- README
- Setup instructions
- Database setup
- Authentication demo
- Availability demo
- Hold/confirmation demo
- Concurrent booking explanation
- Reservation lifecycle demo
- Background-job explanation
- Reporting demo
- Architecture diagram
- Booking workflow diagram
- Concurrency diagram
- Testing instructions
- Freelancer positioning
- Genuine Scalar/API screenshots

Final verification:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
git diff --check
```

---

# 46. Git Workflow

Never implement the entire project directly on `master`.

Workflow:

```text
master
  ↓
phase/01-foundation
  ↓
Pull Request
  ↓
master
  ↓
phase/02-authentication
  ↓
Pull Request
  ↓
...
```

Each PR should contain:

- Clear summary
- Included functionality
- Important business rules
- Security considerations
- Concurrency considerations when relevant
- Tests added
- Validation commands actually run
- Build/test results
- Database migration notes
- Explicit scope boundary

Do not claim a command passed unless it was actually executed.

Suggested branch sequence:

```text
phase/01-foundation
phase/02-authentication
phase/03-refresh-tokens
phase/04-services-resources
phase/05-schedule-management
phase/06-availability-engine
phase/07-booking-concurrency
phase/08-reservation-lifecycle
phase/09-background-reporting
phase/10-portfolio-polish
```

---

# 47. Code Quality Rules

Follow these rules throughout the project:

- Nullable reference types enabled
- Async database APIs
- CancellationToken where useful
- Dependency injection
- Thin controllers
- Business logic in services
- No generic repository abstraction
- No unnecessary inheritance
- No static service locator
- No secrets in source control
- No sensitive values in logs
- No arbitrary dynamic SQL
- Parameterized Dapper queries
- Parameterized raw SQL
- Explicit query/filter rules
- UTC persisted timestamps
- Business timezone explicitly configured
- `TimeProvider` for time-dependent logic
- Clear naming
- Focused methods
- Comments explain why, not obvious what
- Transactions remain short
- No external calls inside booking transactions
- Build with zero errors
- Resolve meaningful warnings
- Tests must protect important business rules

---

# 48. Portfolio Positioning

Suggested portfolio title:

> ASP.NET Core Booking Management API with Concurrency-Safe Reservations

Alternative:

> ASP.NET Core Reservation System with Availability Scheduling & Double-Booking Prevention

Suggested description:

> Production-oriented booking REST API built with ASP.NET Core, EF Core and SQL Server. Includes JWT authentication, resource scheduling, availability calculation, temporary booking holds, transactional reservation workflows, SQL Server concurrency control, background hold expiration, Dapper reporting, validation, automated tests and OpenAPI documentation.

Skills demonstrated:

```text
ASP.NET Core
.NET
C#
REST API
JWT
Authentication
Authorization
Entity Framework Core
SQL Server
Dapper
Database Transactions
Concurrency Control
Scheduling
Reservation Systems
Background Services
TimeProvider
Business Logic
FluentValidation
OpenAPI
Automated Testing
Backend Development
```

The main portfolio differentiator is:

```text
Real scheduling rules
+
Temporary reservation holds
+
Database-level double-booking prevention
+
Concurrent integration testing
+
Background expiration processing
```

This makes the project materially different from a normal CRUD API and from an e-commerce order-management project.

---

# 49. Recommended README Proof Points

The final README should make the project's strongest features visible near the top.

Recommended highlights:

```text
✅ Concurrency-safe booking
✅ SQL Server transactional locking
✅ Double-booking prevention
✅ Availability calculation
✅ Temporary reservation holds
✅ Automatic hold expiration
✅ Reservation confirmation/cancellation/rescheduling
✅ JWT + refresh-token rotation
✅ EF Core + SQL Server
✅ Dapper reporting
✅ Automated integration tests
```

Include a short concurrency example:

```text
Two customers attempt to reserve the same resource/time concurrently.

Request A → 201 Created
Request B → 409 Conflict
```

Explain that this result is enforced at booking time inside the database transaction, not merely by the earlier availability search.

This should be one of the primary screenshots/test examples in the GitHub portfolio.

---

# 50. Final Scope Principle

When deciding whether to add a feature, ask:

```text
Does this feature strengthen the portfolio's proof of
backend scheduling, concurrency, transactional logic,
security, or API quality?
```

If the answer is no, keep it out of Version 1.

The project's value comes from executing a focused booking engine professionally, not from maximizing the number of features.
