<div id="top"></div>
<!--
*** Thanks for checking out the Best-README-Template. If you have a suggestion
*** that would make this better, please fork the repo and create a pull request
*** or simply open an issue with the tag "enhancement".
*** Don't forget to give the project a star!
*** Thanks again! Now go create something AMAZING! :D
-->



<!-- PROJECT SHIELDS -->
<!--
*** I'm using markdown "reference style" links for readability.
*** Reference links are enclosed in brackets [ ] instead of parentheses ( ).
*** See the bottom of this document for the declaration of the reference variables
*** for contributors-url, forks-url, etc. This is an optional, concise syntax you may use.
*** https://www.markdownguide.org/basic-syntax/#reference-style-links
-->
[![Build pre-release](https://github.com/asbjorjo/VeloTimer/actions/workflows/prerelease.yml/badge.svg)](https://github.com/asbjorjo/VeloTimer/actions/workflows/prerelease.yml)
<br/>
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]
[![LinkedIn][linkedin-shield]][linkedin-url]



<h3 align="center">Velo Timer</h3>

  <p align="center">
    An application for turning data from a velodrome timing system into helpful or interesting statistics for riders and coaches.
    <br />
    <!-- <a href="https://github.com/asbjorjo/VeloTimer"><strong>Explore the docs »</strong></a>
    <br />--!>
    <br />
    <a href="http://velotimer.azurewebsites.net/">View Demo</a>
    ·
    <a href="https://github.com/asbjorjo/VeloTimer/issues">Report Bug</a>
    ·
    <a href="https://github.com/asbjorjo/VeloTimer/issues">Request Feature</a>
  </p>
</div>



<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## About The Project

The main purpose of this is to provide a way of turning passing data from the timing system at a velodrome into meaningful data and tools for coaches and riders.
An aside is to produce fun statistics for riders and track owners.

<p align="right">(<a href="#top">back to top</a>)</p>



### Built Using

* [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet)
* [EntityFramework Core](https://docs.microsoft.com/en-us/ef/)
* [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
* [MudBlazor](https://mudblazor.com/)

<p align="right">(<a href="#top">back to top</a>)</p>

<!-- ROADMAP -->
## WebUI Backend for Frontend

`WebUI.Mud` hosts the browser API and owns backend service discovery, aggregation,
Keycloak access, and token refresh. `WebUI.Mud.Client` uses the same service
interfaces, with same-origin HTTP implementations in the browser. Interactive Auto
uses the existing server implementations during prerendering/server execution and
the HTTP implementations when WebAssembly runs. Access and refresh tokens are not
serialized into browser authentication state.

Public reads:

* `GET /api/facilities`
* `GET /api/facilities/{facilityId}/layouts`
* `GET /api/layouts/{layoutId}`
* `GET /api/statistics/samples?cursor=...&isNextPage=false&pageSize=10`

Cookie-authenticated operations:

* `GET /api/facilities/{facilityId}/installations`
* `GET /api/timing/installations` and `GET /api/timing/installations/{id}`
* `PUT /api/timing/installations/{id}` with an `InstallationView` JSON body
* `GET /api/profile/username` and `GET /api/profile/accounts`
* `POST /api/profile/accounts/{provider}/link` with a JSON string containing a local
  or same-origin redirect URI
* `DELETE /api/profile/accounts/{provider}`

API authentication failures return HTTP 401/403 instead of HTML login redirects.
Login, logout, password changes, and provider challenges remain full-page
`/authentication/...` operations. Before a browser mutation, the HTTP handler
fetches `GET /api/antiforgery`, then sends its JSON string token in `X-CSRF-TOKEN`.
Mutations without a valid token are rejected. BFF responses are marked `no-store`.

Run the application through the Aspire AppHost for service discovery and its
Redis, Keycloak, and domain API dependencies. Focused boundary tests run with:

```sh
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --filter FullyQualifiedName~WebUiBffTests
```

## Administration

The administration screens replace the previous admin pages and share the existing
Interactive Auto/BFF infrastructure. Navigation and direct URLs enforce module roles.

* `/admin/facilities` manages facilities; each facility links to its course layouts.
  The layout editor manages named coursepoints, ordered connected segments, lengths
  in metres, reuse of points within a facility, and timing-point links.
* `/admin/timing/installations` manages installations and their facility assignments.
  Each installation includes timing-point management and a coursepoint selector.
  The previous `/admin/installations` URLs remain aliases. Observed hardware identity
  and recorded passings are protected from incompatible edits.
* `/admin/statistics/items` and `/admin/statistics/configurations` manage statistics
  items, Simple coursepoint configurations, and Multi repetition configurations.
  Definitions with dependencies cannot be deleted. Recorded results prevent changes
  to timing semantics; create a new definition instead. Descriptive edits are allowed.
* `/transponders` lets a signed-in user register a MyLaps X2 code, list their own
  ownership periods, end an active period, or permanently delete a selected period.
* `/admin/timing/transponders` lets timing administrators register transponders, edit
  their labels, delete unused transponders, and create, edit, end, or delete anyone's
  ownership periods. Owner IDs are Keycloak subject UUIDs. Ownership editor timestamps
  are UTC. Deleting a transponder is blocked by ownership, passings, or statistics.

All relationship selections are validated again by the owning API. Facilities,
layouts, installations, timing points, and statistics definitions cannot be deleted
while dependent records remain. Cross-module dependency checks use authenticated
HTTP, never another module's database. If a required service is unavailable, the
operation fails closed. These preflight checks are not a distributed transaction;
local transactions and database constraints protect local invariants.

Coursepoints may be shared between layouts in the same facility. Timing links must
remain within that facility, and a timing point can link to only one coursepoint.
Layout geometry used by statistics definitions or recorded samples cannot be changed;
create a new layout instead. Relationship cache entries are invalidated after edits.
Layout or timing-point saves that succeed before a separate link operation fails
report the partial save explicitly and retain the saved ID for retry.

Keycloak realm roles are mapped at authentication into explicit `velotime_permission`
claims. The `velotime-admin` permission grants all modules; `facilities-admin`,
`timing-admin`, and `statistics-admin` grant their respective modules. Timing admins
also receive the scoped coursepoint/timing-point linking permission, without broader
Facilities write access. APIs, BFF routes, pages, and navigation all use named
permission policies; they do not authorize with role checks. Ordinary accounts do not
receive administrator permissions automatically.

For a new realm, the checked-in import declares these roles and includes realm roles
in the WebUI ID token. Existing Keycloak realms are not updated by editing the import:
create the roles, add the WebUI realm-role mapper to ID/access tokens, assign the
appropriate role to administrators, then sign out and sign in again. No admin API
credentials are sent to the browser.

The WebUI also requests the optional `velotime:all` client scope, which adds the
`velotime.api` audience and the user subject required by the domain APIs. Associate this scope
with the `velotime.webui` client when updating an existing realm. This audience scope
does not grant administrator roles or bypass ownership checks. Sign out and back in
after changing scope assignments so newly issued access tokens contain the audience.
On realms without Keycloak's built-in `basic` scope, include the `oidc-sub-mapper`
subject mapper from the import so access tokens also carry the user's `sub` claim.

Ownership mutations use a PostgreSQL transaction advisory lock per transponder and
the existing unique system/SystemId index. Self-service endpoints derive the owner
from the authenticated subject; they do not accept an owner ID. Ending retains the
ownership period; deleting removes only that period, not the transponder or results.

The Timing and Facilities API test factories can target a **disposable test database
only** using `VELOTIME_TEST_POSTGRES`. Their setup deletes and recreates that database;
run these suites separately when sharing a test database. With this setting,
competing-claim tests run concurrently against real PostgreSQL and link mutations
exercise PostgreSQL transaction locks. The default SQLite test run checks the same
ownership conflict sequentially.

## Roadmap

- [] User authentication
    - [] Authorization
    - [] Strava account linking
    - [] Social media account linking

See the [open issues](https://github.com/asbjorjo/VeloTimer/issues) for a full list of proposed features (and known issues).

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- CONTRIBUTING -->
## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".
Don't forget to give the project a star! Thanks again!

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- LICENSE -->
## License

No license yet. See `LICENSE.txt` later.

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- CONTACT -->
## Contact

Asbjørn L. Johansen - [@asbjorjo](https://twitter.com/asbjorjo) - asbjorjo@gmail.com

Project Link: [https://github.com/asbjorjo/VeloTimer](https://github.com/asbjorjo/VeloTimer)

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- ACKNOWLEDGMENTS -->
## Acknowledgments

* [Best-README-Template](https://github.com/othneildrew/Best-README-Template)

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://www.markdownguide.org/basic-syntax/#reference-style-links -->
[contributors-shield]: https://img.shields.io/github/contributors/asbjorjo/VeloTimer.svg?style=for-the-badge
[contributors-url]: https://github.com/asbjorjo/VeloTimer/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/asbjorjo/VeloTimer.svg?style=for-the-badge
[forks-url]: https://github.com/asbjorjo/VeloTimer/network/members
[stars-shield]: https://img.shields.io/github/stars/asbjorjo/VeloTimer.svg?style=for-the-badge
[stars-url]: https://github.com/asbjorjo/VeloTimer/stargazers
[issues-shield]: https://img.shields.io/github/issues/asbjorjo/VeloTimer.svg?style=for-the-badge
[issues-url]: https://github.com/asbjorjo/VeloTimer/issues
[license-shield]: https://img.shields.io/github/license/asbjorjo/VeloTimer.svg?style=for-the-badge
[license-url]: https://github.com/asbjorjo/VeloTimer/blob/master/LICENSE.txt
[linkedin-shield]: https://img.shields.io/badge/-LinkedIn-black.svg?style=for-the-badge&logo=linkedin&colorB=555
[linkedin-url]: https://linkedin.com/in/asbjorjo
