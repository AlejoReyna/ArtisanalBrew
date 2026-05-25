# Google Gemini Context Prompt — Design “My Profile” for ThisCafeteria

Copy everything below the horizontal rule into Gemini (or attach this file). Ask Gemini to produce a **full implementation plan** and optionally **code artifacts** that fit this repository exactly—not a generic profile page.

---

## Your role

You are a senior .NET architect and product engineer designing the **My Profile** customer area for **ThisCafeteria** (branded in the UI as **Artisanal Brew**). You must respect the existing **Clean Architecture**, **Blazor interactive server** patterns, **ASP.NET Core Identity**, **wallet-based login (BSC Testnet)**, and the **domain model** already in the database. Do not invent a parallel user system.

Deliverables expected from you:

1. **Product scope** — sections, user stories, edge cases.
2. **Business rules** — explicit validation, authorization, and data consistency rules (numbered).
3. **Domain/application changes** — new or extended entities, DTOs, services, validators, repository methods.
4. **API surface** — REST endpoints under `ThisCafeteria.Web/Controllers` with request/response shapes.
5. **Blazor UI** — route, layout, components, CSS conventions, nav integration.
6. **Identity bridge** — how `ApplicationUser` links to `UserProfile` for **wallet users** and **email/admin users** (this is the hardest gap today).
7. **Migration/seed impact** — only if schema changes are justified.
8. **Test plan** — unit + integration scenarios.
9. **Phased rollout** — MVP vs follow-ups aligned with scaffolded pages (`/orders`, `/cart`, `/checkout`).

Use **complete sentences**. Be **explicit and extensive**. Reference file paths and layer names from this document.

---

## Project overview

**ThisCafeteria** is an ASP.NET Core **.NET 10** reconstruction of a coffee storefront (“This Cafeteria Doesn't Exist”). The legacy Next.js app lives at `thisCafeteriaDoesntExist/` and is **read-only reference** for UX ideas (profile avatar in navbar dropdown, order link, logout)—**do not** copy its JWT/localStorage auth into the new stack.

| Layer | Project path | Responsibility |
|--------|----------------|----------------|
| Domain | `src/ThisCafeteria.Domain` | Entities, enums |
| Application | `src/ThisCafeteria.Application` | DTOs, FluentValidation, service interfaces, application services, repository **contracts** |
| Infrastructure | `src/ThisCafeteria.Infrastructure` | EF Core `AppDbContext`, configurations, repositories, Identity `ApplicationUser`, AWS placeholders (S3, SQS, SES) |
| Web | `src/ThisCafeteria.Web` | Blazor pages, API controllers, Identity cookie auth, wallet JS module |
| Worker | `src/ThisCafeteria.Worker` | Background order-processing placeholder (SQS) |

**Database:** PostgreSQL via EF Core. Local Docker maps host port **5433**.

**Auth today:**

- **Primary customer path:** BNB Smart Chain **Testnet** wallet sign-in (`/api/wallet-auth/challenge`, `/api/wallet-auth/verify`, `/api/wallet-auth/logout`).
- **Admin path:** Email/password Identity user seeded from config (`Authentication:AdminEmail`, `Authentication:AdminPassword`).
- Blazor uses `AddCascadingAuthenticationState()`, cookie auth, `[Authorize]`, `AuthorizeView` in `NavMenu.razor`.

**Brand / UX:** Warm editorial coffee shop (`#fbf9f4` background, `#2d2421` ink). Public layout: `MainLayout.razor` + `NavMenu.razor`. Rich pages use CSS classes like `page-intro`, `eyebrow`, `lede`, `button button--dark`, `stagger-container`, `data-animate="fade-up"` (see `Transparency.razor` and `wwwroot/app.css`).

---

## Solution layout (how code is organized)

```
ThisCafeteria/
├── src/
│   ├── ThisCafeteria.Domain/
│   ├── ThisCafeteria.Application/
│   ├── ThisCafeteria.Infrastructure/
│   ├── ThisCafeteria.Web/
│   └── ThisCafeteria.Worker/
├── tests/
│   ├── ThisCafeteria.UnitTests/
│   └── ThisCafeteria.IntegrationTests/
├── docs/                          ← you are reading a file here
├── docker-compose.yml
├── .env.example
└── README.md
```

**DI registration:**

- `Program.cs` → `AddApplication()`, `AddInfrastructure(configuration)`, Identity, authorization policy `RequireAdmin`.
- `Application/DependencyInjection.cs` registers validators + `IProductService`, `IOrderService`, `ITransparencyService`.
- `Infrastructure/DependencyInjection.cs` registers `AppDbContext`, repositories, placeholder AWS services.

**Adding a feature (required pattern):**

1. Extend or add entity in **Domain** (only if persisted data changes).
2. Add DTOs + validator(s) + `IProfileService` (name illustrative) in **Application**.
3. Add `IUserProfileRepository` implementation in **Infrastructure** if queries are non-trivial.
4. Register service in `Application/DependencyInjection.cs`.
5. Expose **API controller** in **Web** for Blazor/interop or future clients.
6. Add Blazor page `@page "/profile"` (or `/account`) with `@rendermode InteractiveServer` where user input is needed.
7. Wire **NavMenu** authorized block (today: user icon with **no link**).

---

## Domain model (existing — do not redesign from scratch)

### `UserProfile` (`Domain/Entities/UserProfile.cs`)

```csharp
public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;      // unique index, max 320
    public string DisplayName { get; set; } = string.Empty; // max 160, required
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Order> Orders { get; set; } = [];
    public Cart? Cart { get; set; }
}
```

`UserRole` enum: `Customer`, `Admin`.

**There is no avatar URL, phone, address, or preferences table today.** If My Profile needs avatar upload, specify whether to add `AvatarUrl` on `UserProfile` or store only in S3 keyed by `UserProfileId`, and use `IS3StorageService` (currently a placeholder in Infrastructure).

### `ApplicationUser` (`Infrastructure/Identity/ApplicationUser.cs`)

Extends `IdentityUser`:

```csharp
public sealed class ApplicationUser : IdentityUser
{
    public Guid? UserProfileId { get; set; }      // FK to domain profile — optional today
    public string? WalletAddress { get; set; }    // checksum, unique when set
    public int? WalletChainId { get; set; }
    public DateTimeOffset? WalletVerifiedAt { get; set; }
}
```

Wallet users are created in `WalletAuthController.FindOrCreateWalletUserAsync` with `UserName = checksum address`, **no email**, **no `UserProfileId`**.

Admin seeder (`AdminIdentitySeeder`) creates **both** `UserProfile` and `ApplicationUser` with `UserProfileId` set.

### Commerce aggregates tied to profile

| Entity | Key fields | Relation to profile |
|--------|------------|---------------------|
| `Cart` | `UserProfileId` (1:1) | One cart per profile |
| `CartItem` | product refs, qty, price | Child of cart |
| `Order` | `UserProfileId`, `OrderNumber`, `OrderStatus`, totals | Many per profile |
| `OrderItem` | line items | Child of order |
| `Receipt` | `OrderId`, `FileUrl`, `EmailSentAt` | 1:1 with order (S3 placeholder) |
| `TransparencyRecord` | per order line, BSC metadata, `Status`, hashes | Created on order create |

`OrderStatus`: `Pending`, `Processing`, `Completed`, `Cancelled`.

**Order business logic already implemented** (`OrderService`):

- Tax rate **16%** on subtotal.
- Order number: `TC-{yyyyMMddHHmmss}-{random 1000-9999}`.
- On create: persists order + calls `ITransparencyService.CreatePendingRecordsForOrderAsync` (per line item, shared order hash, chain 97, status `"Pending"`).

### `Product`

Catalog for shop; not owned by user profile except via cart/orders.

---

## Critical integration gap (you must solve in design)

`OrdersController.GetMyOrders` resolves the customer like this:

```csharp
var email = User.Identity?.Name;
var userProfileId = await dbContext.UserProfiles
    .Where(profile => profile.Email == email)
    .Select(profile => profile.Id)
    .FirstOrDefaultAsync();
```

**Wallet-authenticated users** have `User.Identity.Name` = **wallet address**, not email. They typically have **no `UserProfile` row**. Result: API returns **empty orders** even after checkout.

**My Profile design must include a canonical resolver:**

`ApplicationUser` (from `UserManager` / `HttpContext.User`) → `UserProfileId` → `UserProfile`.

Proposed rules (refine and document):

1. On wallet verify (or first profile access), **ensure** a `UserProfile` exists:
   - `Email`: synthetic stable value e.g. `{wallet}@wallet.thiscafeteria.local` **or** nullable email column migration—justify choice.
   - `DisplayName`: default truncated address `0x1234…abcd` until user edits.
   - `Role`: `Customer`.
2. Set `ApplicationUser.UserProfileId` and persist.
3. Ensure **one `Cart`** row exists for that profile (empty cart).
4. All order/cart APIs use **`UserProfileId` from claims or DB**, never raw email match alone.

Admin users: keep existing link via seeded email profile.

---

## Authentication & wallet flows (existing behavior)

### Server: `WalletAuthController` (`/api/wallet-auth`)

- **POST challenge:** validates address, stores nonce in `IMemoryCache` (5 min), returns EIP-4361-style multi-line message (app name, address, chain 97, URI, nonce, issued/expiry).
- **POST verify:** recovers signer via Nethereum, creates/updates `ApplicationUser`, signs in with cookie (`isPersistent: true`).
- **POST logout:** signs out, redirects `/`.

Chain config: `BnbTestnetOptions` from `appsettings.json` section `BnbTestnet` (chain id **97**, explorer `https://testnet.bscscan.com/`).

### Client: `wwwroot/js/walletAuth.js`

- `loginWithWallet(walletName)` → MetaMask/Coinbase/etc. via `window.ethereum`.
- Switches/adds BSC Testnet (`0x61`).
- `personal_sign` on challenge message.

### Nav (`Components/Layout/NavMenu.razor`)

- **Not authorized:** “Login” opens wallet modal (MetaMask, Coinbase, WalletConnect, Keplr).
- **Authorized:** profile **icon only** (SVG, no `href`), logout form POST to `/api/wallet-auth/logout`.
- Cart icon present but cart page is scaffolded.

**My Profile entry point:** link the icon (and/or add “Account” nav) to `@page "/profile"` with `[Authorize]`.

---

## Existing API & pages relevant to profile

| Route / API | Status | Notes |
|-------------|--------|-------|
| `GET /api/orders/me` | Implemented | Broken for wallet users until profile bridge |
| `POST /api/orders` | Implemented | Requires `CreateOrderRequest.UserProfileId` — caller must send correct id |
| `/orders` Blazor | Scaffold | Placeholder text only |
| `/register` | Scaffold | Identity configured, no UI |
| `/cart`, `/checkout` | Scaffold | Checkout mentions receipt + queue |
| `/transparency` | **Complete** | Public list via `ITransparencyService` — user-specific subset may belong on profile |
| `/admin/*` | Scaffold | `[Authorize(Policy = "RequireAdmin")]` via `AdminLayout` |

Controllers pattern: thin controllers, delegate to application services (`ProductsController`, `OrdersController`).

---

## Legacy UX reference (Next.js — inspiration only)

`thisCafeteriaDoesntExist/components/Navbar.tsx` logged-in dropdown:

- Circular **profile image** (file input → base64 → `localStorage`) — **not** acceptable for production in .NET app; replace with S3 + `UserProfile` field if needed.
- Shows **username** from JWT/localStorage.
- Links: **Orders**, **Log Out** (clears localStorage).

Map intent to Blazor:

| Legacy | ThisCafeteria equivalent |
|--------|---------------------------|
| Avatar | Optional `AvatarUrl` + S3 upload endpoint |
| Username | `UserProfile.DisplayName` or shortened wallet |
| Orders | `/orders` or profile tab fed by `GET /api/orders/me` |
| Logout | Existing POST logout |

---

## What “My Profile” should contain (business scope)

Design all sections with **authorization**: only the signed-in user’s data unless `UserRole.Admin` (admin profile management is out of scope unless you explicitly add admin “view customer” later).

### 1. Identity summary (read-only + editable fields)

**Display:**

- Display name (editable).
- Account type: Wallet / Email (derived from `ApplicationUser`).
- Wallet address (checksum, copy button), chain name, `WalletVerifiedAt`.
- Link to BscScan address URL when wallet present.
- Member since (`UserProfile.CreatedAt`).
- Role badge (`Customer` vs `Admin`) — hide elevated actions from customers.

**Edit rules:**

- `DisplayName`: required, 2–160 chars, trim whitespace, no HTML.
- Email: for wallet-only users, show synthetic email as read-only or hide; for email users, show read-only (Identity owns email changes—defer password/email change to separate Identity UI if needed).
- Do **not** allow users to change `Role` or `WalletAddress` via profile form (wallet address changes = re-auth flow only).

### 2. Order history (core commerce)

**Read:**

- List orders for `UserProfileId` descending by `CreatedAt`.
- Columns/cards: `OrderNumber`, `Status`, `Total`, `CreatedAt`, item count.
- Expand row: line items (`ProductName`, `Quantity`, `UnitPrice`, line total).
- Link to transparency: show `TransparencyRecords` per order (status, short hash, explorer link when confirmed).

**Business rules:**

- Pagination or “last 20” default.
- Empty state CTA → `/products`.
- Status labels map 1:1 to `OrderStatus` enum.

**Do not** reimplement tax calculation on profile; consume `OrderDto` from `IOrderService.GetOrdersForUserAsync`.

### 3. Cart snapshot (optional MVP tab)

- If `Cart` exists, show item count and link to `/cart`.
- Full cart editing stays on `/cart` page; profile may show summary only.

### 4. Transparency & trust (differentiator)

Project differentiator: **on-chain transparency** for purchases.

- Profile section: “Your on-chain receipts” — filter `TransparencyRecord` where `Order.UserProfileId == current user`.
- Show `Pending` vs confirmed with `ExplorerUrl`.
- Clarify copy: records created at checkout; confirmation async (worker future).

### 5. Preferences (phase 2 — specify now, implement later)

Examples: email notifications for receipts (`Receipt.EmailSentAt`), newsletter opt-in, default store location. **No tables exist** — list as future entities if needed.

### 6. Security & session

- Logout: reuse `POST /api/wallet-auth/logout`.
- Show last wallet verification time.
- No secret keys in UI.

---

## Application layer design expectations

Follow `ProductService` / `OrderService` patterns:

- Interface in `Application/Services/IProfileService.cs` (or `IUserProfileService.cs`).
- DTOs in `Application/DTOs/` e.g. `UserProfileDto`, `UpdateUserProfileRequest`, `ProfileDashboardDto` (profile + recent orders + stats).
- FluentValidation for updates in `Application/Validation/`.
- Repository interface only if queries exceed what `AppDbContext` should expose from Web (prefer repository for consistency: `IUserProfileRepository`).

**Suggested service methods:**

```text
GetProfileForCurrentUserAsync(userId or principal) → ProfileDashboardDto
UpdateDisplayNameAsync(userProfileId, request) → UserProfileDto
EnsureProfileLinkedAsync(applicationUserId) → UserProfileDto  // idempotent bridge
GetOrderHistoryAsync(userProfileId, skip, take) → wraps IOrderService
GetTransparencyForUserAsync(userProfileId, take) → query orders/transparency
```

**Controller:** `ProfileController` or extend pattern:

```text
GET  /api/profile/me           [Authorize]
PATCH /api/profile/me         [Authorize]  body: { displayName }
GET  /api/profile/me/orders   [Authorize]  optional alias to orders/me after fix
```

Blazor page can call service via `@inject IProfileService` **or** `HttpClient` to API—existing pages inject application services directly (`Transparency.razor` uses `ITransparencyService`). **Prefer inject application service** for interactive server components to avoid double HTTP hop.

---

## UI / Blazor specifications

- **Route:** `@page "/profile"` (and authorize).
- **PageTitle:** `My Profile | Artisanal Brew`.
- **Layout:** `MainLayout` (public), not `AdminLayout`.
- **Render mode:** `InteractiveServer` for edit form.
- **Structure:** mirror `Transparency.razor`:
  - `section.page-intro` with eyebrow + h1 + lede.
  - Cards/sections with `data-animate="fade-up"`.
  - Use `AuthorizeView` redirect or `NavigationManager` to home if challenge needed.
- **NavMenu change:** wrap profile icon in `<a href="profile">` or `<NavLink href="profile">` for authorized users.
- **Accessibility:** form labels, `aria-live` for save errors, focus management on validation.

**States to design:**

- Loading skeleton.
- Save success toast or inline message.
- Validation errors from FluentValidation.
- Wallet user first visit (auto-provision profile) — show welcome banner once.

---

## AWS & worker touchpoints (profile-related)

Infrastructure placeholders:

- `IS3StorageService` — avatar/receipt files.
- `IEmailSender` (SES) — send receipt to profile email when order completes.
- `ISqsMessagePublisher` — checkout publishes order processing.

Profile UI should **not** block on AWS implementation; define interfaces and stub behavior consistent with existing placeholders.

When checkout is implemented, **CreateOrder** must use the **resolved `UserProfileId`** for the current user, not a client-supplied GUID (security: reject mismatch between JWT user and request body).

---

## Configuration & environment

From `.env.example` / `appsettings.json`:

- `ConnectionStrings__DefaultConnection`
- `Authentication__AdminEmail`, `Authentication__AdminPassword`
- `BnbTestnet:*`
- `AWS:*` (Region, S3 bucket, SQS URL, SES sender)

Serilog to console. Swagger in Development. Health at `/health`.

---

## Testing expectations

Align with `tests/ThisCafeteria.UnitTests` and `IntegrationTests` (Testcontainers for Postgres):

1. Wallet user without profile → `EnsureProfileLinked` creates profile + cart.
2. Update display name validation failures.
3. `GetOrdersForUser` returns orders only for linked profile.
4. Unauthorized `GET /api/profile/me` → 401.
5. User A cannot PATCH user B’s profile.

---

## Anti-patterns (forbidden)

- Duplicating `UserProfile` as a Blazor-only model disconnected from EF entities.
- Storing profile images in `localStorage` only.
- Matching orders by `User.Identity.Name` email **without** wallet bridge.
- Putting EF queries directly in Razor `.razor` `@code` blocks—use application services.
- Creating a second authentication scheme.
- Breaking wallet login flow or BSC Testnet chain id 97.

---

## Output format requested from Gemini

Structure your answer as:

1. **Executive summary** (5–10 sentences).
2. **User stories** (Given/When/Then).
3. **Business rules catalog** (numbered BR-001…).
4. **Data model changes** (if any) with EF migration notes.
5. **Sequence diagrams** (text or Mermaid) for: first wallet login → profile provision; view profile; update display name; load order history.
6. **API contract table** (method, path, auth, body, response, errors).
7. **Application service & repository method list** with signatures (C#).
8. **Blazor component tree** and CSS class plan.
9. **NavMenu & Orders page integration**.
10. **Fix for `OrdersController` identity resolution** (concrete code-level approach).
11. **MVP vs Phase 2** task list with file paths to create/modify.
12. **Open questions** only if truly blocked—otherwise make reasonable defaults and state them.

Optimize for implementability by a developer who has this repo open and will paste your plan into Cursor/IDE next.

---

## Key file paths quick reference

| Concern | Path |
|---------|------|
| User profile entity | `src/ThisCafeteria.Domain/Entities/UserProfile.cs` |
| Identity user | `src/ThisCafeteria.Infrastructure/Identity/ApplicationUser.cs` |
| EF context | `src/ThisCafeteria.Infrastructure/Persistence/AppDbContext.cs` |
| Profile EF config | `src/ThisCafeteria.Infrastructure/Persistence/Configurations/UserProfileConfiguration.cs` |
| Wallet auth API | `src/ThisCafeteria.Web/Controllers/WalletAuthController.cs` |
| Orders API | `src/ThisCafeteria.Web/Controllers/OrdersController.cs` |
| Order service | `src/ThisCafeteria.Application/Services/OrderService.cs` |
| Nav / login UI | `src/ThisCafeteria.Web/Components/Layout/NavMenu.razor` |
| Reference complete page | `src/ThisCafeteria.Web/Components/Pages/Transparency.razor` |
| Global styles | `src/ThisCafeteria.Web/wwwroot/app.css` |
| Wallet JS | `src/ThisCafeteria.Web/wwwroot/js/walletAuth.js` |
| Program startup | `src/ThisCafeteria.Web/Program.cs` |
| Admin seed | `src/ThisCafeteria.Infrastructure/Identity/AdminIdentitySeeder.cs` |
| Legacy navbar profile UX | `thisCafeteriaDoesntExist/components/Navbar.tsx` |

---

*End of Gemini context prompt.*
