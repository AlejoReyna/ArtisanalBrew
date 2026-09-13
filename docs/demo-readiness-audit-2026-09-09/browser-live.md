# Live browser vs cold curl — distinction (2026-09-09)

## Live interactive browser session (parent audit)

Interactive Blazor session against `https://cafe.alexisrs.dev` while exercising must-show journeys:

| Surface | Session result | Notes |
|---|---|---|
| Homepage / product list / product detail | OK initially | Catalog cards + Sepolia messaging present |
| Add to Cart → `/products` | **HTTP 500** + Blazor unhandled error | Browse-only remained safe before cart mutation |
| Cart / checkout | **Broken / unverified** | Add click preceded products 500; checkout not completed; no payment attempted |
| `/orders` | **HTTP 500** during session | Orders journey unverified interactively |
| `/staking` | **HTTP 500** in session | Login showed no wallet modal + Blazor error; Sepolia copy still present on pages that rendered |
| `/procurement-lab` | Intro OK; wallet CTA → Blazor error | Deliver path submits placeholder `IPFS_HASH_HERE` (code); MCP stubs return empty 200 |
| `/api/chains` | OK | Sepolia default + multi-chain capabilities visible |
| `/robots.txt` | 500 | Non-journey |
| Error pages | Show **Development Mode** | Leak of env presentation |
| Profile (anonymous) | Redirects to login | Expected |

Screenshots: `screenshots/01-homepage.png` … `11-robots-500.png` (notably `04`/`05` products-500, `07` staking-500, `09` orders-500).

## Post-audit cold curl (parent)

After the interactive session, cold GETs returned **200** again for:

`/`, `/products`, `/orders`, `/staking`, `/procurement-lab`, `/api/chains`, `/health/ready`

## Interpretation (status language for final report)

- Session **500s are intermittent / Blazor-circuit failures**, not permanent SSR death.
- Cold curl **does not** prove interactive cart, checkout, wallet connect, staking actions, or agentic wallet CTAs.
- Must-show interactive journeys that failed or were unverified in-browser remain **Broken** or **Unverified** for demo readiness even though SSR pages may serve 200 when cold.
- Safe cold claim: storefront **browse** and health/chains API are available between failures; do not claim cart→pay or staking connect as verified on live.
