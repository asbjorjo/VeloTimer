# Change Log

All notable changes to this project will be documented in this file. See [versionize](https://github.com/versionize/versionize) for commit guidelines.

## Unreleased

### Features

* **timing:** enqueue ownership change events atomically with ownership writes and deliver through a PostgreSQL transactional outbox.
* **webui:** replace administration pages with Facilities/course-layout, installation/timing-point, transponder ownership, and statistics definition management.
* **authorization:** map identity claims to explicit permission claims and enforce named policies for modules, scoped coursepoint linking, and ownership isolation.
* **timing:** allow signed-in users to register transponders and end or delete their own ownership periods; allow timing administrators to manage all ownerships.
* **api:** add validated relationship editing, cross-module dependency checks, deletion safeguards, and cache invalidation.

### Bug Fixes

* **webui:** stop the public statistics page from redirecting back to its previous selection, which blocked leaving the page and changing facility.
* **timing:** return transponders instead of installations from transponder queries and persist custom transponder labels.
* **facilities:** return an empty layout list for an existing facility without layouts.
* **authentication:** request the configured API-audience scope when forwarding signed-in users to protected domain APIs.

<a name="2.1.0-alpha.23"></a>
## [2.1.0-alpha.23](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.23) (2026-11-01)

### Features

* **passing-migration:** add the plan ([f8c4b77](https://www.github.com/asbjorjo/VeloTimer/commit/f8c4b77925053b2844a97cfafa29dee778c38354))

<a name="2.1.0-alpha.22"></a>
## [2.1.0-alpha.22](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.22) (2026-10-31)

### Features

* **cache:** add phase-three versioned module caching ([#113](https://www.github.com/asbjorjo/VeloTimer/issues/113)) ([829b5cd](https://www.github.com/asbjorjo/VeloTimer/commit/829b5cd2abd52619789723713734680691b31c71))

<a name="2.1.0-alpha.21"></a>
## [2.1.0-alpha.21](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.21) (2026-10-31)

### Bug Fixes

* **webui:** merge login issuer correction into v2 ([f613190](https://www.github.com/asbjorjo/VeloTimer/commit/f613190d7574a83e8a5e79b46c463cbda365c58f))
* **webui:** preserve validated issuer in login tickets ([936b0cb](https://www.github.com/asbjorjo/VeloTimer/commit/936b0cbbdf7724294e661d8cb4e7a78d0578f91f))

<a name="2.1.0-alpha.20"></a>
## [2.1.0-alpha.20](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.20) (2026-10-31)

### Bug Fixes

* **webui:** cache preferences and isolate lookup snapshots ([175bf7e](https://www.github.com/asbjorjo/VeloTimer/commit/175bf7e3a3df80d31aadc84717ba496b66dae705))
* **webui:** merge isolated UI caching into v2 ([b796c42](https://www.github.com/asbjorjo/VeloTimer/commit/b796c423ff8b1d0d14ee3d39551791766695ad50))

<a name="2.1.0-alpha.19"></a>
## [2.1.0-alpha.19](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.19) (2026-10-31)

### Bug Fixes

* **cache:** merge backend cache corrections into v2 ([3939625](https://www.github.com/asbjorjo/VeloTimer/commit/393962576c560d365b80af9986ca7beba7c710d4))
* **cache:** unify backend invalidation and correct cache behavior ([1e83754](https://www.github.com/asbjorjo/VeloTimer/commit/1e83754f2188c62a2507c06c27725156b0d4bb89))

<a name="2.1.0-alpha.18"></a>
## [2.1.0-alpha.18](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.18) (2026-10-31)

### Bug Fixes

* **webui:** derive public statistics selection from the URI ([8aaee9a](https://www.github.com/asbjorjo/VeloTimer/commit/8aaee9ad851b40b7e845d65cc2dee6a8f5cf05e3))

<a name="2.1.0-alpha.17"></a>
## [2.1.0-alpha.17](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.17) (2026-10-31)

### Features

* **preferences:** add user units and time display settings ([#112](https://www.github.com/asbjorjo/VeloTimer/issues/112)) ([a08ed9e](https://www.github.com/asbjorjo/VeloTimer/commit/a08ed9e8dee1cc1335b4fc04f083131e541184a6))

<a name="2.1.0-alpha.16"></a>
## [2.1.0-alpha.16](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.16) (2026-10-31)

### Bug Fixes

* **chart:** add facilities API service mapping to stats ([2c43b90](https://www.github.com/asbjorjo/VeloTimer/commit/2c43b90e204b7f27877214aaebd43f4890f7014f))

<a name="2.1.0-alpha.15"></a>
## [2.1.0-alpha.15](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.15) (2026-10-31)

### Features

* **statistics:** add public filtered statistics views ([#111](https://www.github.com/asbjorjo/VeloTimer/issues/111)) ([60add28](https://www.github.com/asbjorjo/VeloTimer/commit/60add2823e1a7b1246e6ba9ba14d4f142cb4f7b8))

<a name="2.1.0-alpha.14"></a>
## [2.1.0-alpha.14](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.14) (2026-10-31)

### Bug Fixes

* **ui:** remove login item from side menu ([0446357](https://www.github.com/asbjorjo/VeloTimer/commit/0446357777845b41a2a8aa4602149733085ca2de))

<a name="2.1.0-alpha.13"></a>
## [2.1.0-alpha.13](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.13) (2026-10-31)

### Bug Fixes

* **users-migration:** normalize rider names before creating Keycloak users ([824130d](https://www.github.com/asbjorjo/VeloTimer/commit/824130d368b13e0f7fbfb6f783b92a17dcd13bd0))

<a name="2.1.0-alpha.12"></a>
## [2.1.0-alpha.12](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.12) (2026-10-30)

### Bug Fixes

* **users-migration:** log and skip riders whose identity lookup fails ([2779a45](https://www.github.com/asbjorjo/VeloTimer/commit/2779a458ee4e5f1cf77532319efb33d23116d281))

<a name="2.1.0-alpha.11"></a>
## [2.1.0-alpha.11](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.11) (2026-10-30)

### Bug Fixes

* **users-migration:** make ownership and profile migration retry-safe ([2081b32](https://www.github.com/asbjorjo/VeloTimer/commit/2081b3207e9566bef03bb0287f7109ffdf922cbb))

<a name="2.1.0-alpha.10"></a>
## [2.1.0-alpha.10](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.10) (2026-10-30)

### Features

* **charts:** run schema migrations as release hooks ([b7c027c](https://www.github.com/asbjorjo/VeloTimer/commit/b7c027c801dfe1fb3d6c6a5a3523651571343d56))

<a name="2.1.0-alpha.9"></a>
## [2.1.0-alpha.9](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.9) (2026-10-30)

### Features

* **webui:** localize data grid pager ([6f832a5](https://www.github.com/asbjorjo/VeloTimer/commit/6f832a51b37b610f3e89c3e4d7827d8f6d7f9903))

<a name="2.1.0-alpha.8"></a>
## [2.1.0-alpha.8](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.8) (2026-10-30)

### Features

* **webui:** improve app bar profile and language controls ([4a183b8](https://www.github.com/asbjorjo/VeloTimer/commit/4a183b82cb5d81db8ff3a7bc6be30e7f52dde17f))
* **webui:** move auth actions to top bar ([832bd1f](https://www.github.com/asbjorjo/VeloTimer/commit/832bd1f4ec757f44decee3266cd6a1e55bf4c54f))

<a name="2.1.0-alpha.7"></a>
## [2.1.0-alpha.7](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.7) (2026-10-30)

### Features

* frontend localization and Profiles preferences ([#110](https://www.github.com/asbjorjo/VeloTimer/issues/110)) ([a5a80f1](https://www.github.com/asbjorjo/VeloTimer/commit/a5a80f14b04af4077b8bce7444b4254ff6e217bb))

<a name="2.1.0-alpha.6"></a>
## [2.1.0-alpha.6](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.6) (2026-10-30)

### Features

* **transponders:** use server-side MudDataGrid ([1ea4a81](https://www.github.com/asbjorjo/VeloTimer/commit/1ea4a813cab24872ed4b3c7380f429d66da00321))

<a name="2.1.0-alpha.5"></a>
## [2.1.0-alpha.5](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.5) (2026-10-30)

### Features

* **transponders:** add column filtering and sorting ([03eb8df](https://www.github.com/asbjorjo/VeloTimer/commit/03eb8dfe5ac858df014e3897a2759e772a169308))

<a name="2.1.0-alpha.4"></a>
## [2.1.0-alpha.4](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.4) (2026-10-30)

### Features

* **transponders:** add ownership-scoped status pages ([74b2cb8](https://www.github.com/asbjorjo/VeloTimer/commit/74b2cb80e1be135fa68a6db8ddd19674a51e60cc))

<a name="2.1.0-alpha.3"></a>
## [2.1.0-alpha.3](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.3) (2026-10-29)

### Bug Fixes

* Use correct method to get facility ([a83e22d](https://www.github.com/asbjorjo/VeloTimer/commit/a83e22d4bc719a0a2e733581fa69d8e5c3ce6452))

<a name="2.1.0-alpha.2"></a>
## [2.1.0-alpha.2](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.2) (2026-10-29)

### Features

* **statistics:** reconcile profiles on ownership changes ([5043e48](https://www.github.com/asbjorjo/VeloTimer/commit/5043e48a6ba3c002bafbdd124216fba1dbbed672))
* **timing:** emit ownership changes through transactional outbox ([bbd48c8](https://www.github.com/asbjorjo/VeloTimer/commit/bbd48c8736f70073848e838faba3a9708f694d7f))

<a name="2.1.0-alpha.1"></a>
## [2.1.0-alpha.1](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.1) (2026-10-29)

### Features

* **timing:** expose transponder last-seen timestamps ([b20e9c1](https://www.github.com/asbjorjo/VeloTimer/commit/b20e9c1c6a298e2b9aa1c8de60e3167a2ddc524e))
* **timing:** paginate transponder management server-side ([ee1e721](https://www.github.com/asbjorjo/VeloTimer/commit/ee1e72127e9c2d0a9ec967a475a2e92aab4dd985))
* **web:** show transponder last seen in administration ([303e73f](https://www.github.com/asbjorjo/VeloTimer/commit/303e73fa52bb7bfe6df9efadc883538f9aaba3ae))

<a name="2.1.0-alpha.0"></a>
## [2.1.0-alpha.0](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v2.1.0-alpha.0) (2026-10-29)

### Features

* Add authentication to modules ([89b9df6](https://www.github.com/asbjorjo/VeloTimer/commit/89b9df66b7d9f1bb5ab2a012e03a728ebea1cc7d))
* Add client for keycloak account API ([9525e8f](https://www.github.com/asbjorjo/VeloTimer/commit/9525e8ff1b4214b67a6949537af290d297371b48))
* Allow unlinking providers ([ae38565](https://www.github.com/asbjorjo/VeloTimer/commit/ae38565f05a36240904b3e42b7aa557daff130da))
* Auto-generate all clients on build ([c889c91](https://www.github.com/asbjorjo/VeloTimer/commit/c889c91501b3687031ac7a9497f3aa9f02de11e6))
* Auto-generate API client ([abcd444](https://www.github.com/asbjorjo/VeloTimer/commit/abcd44443f08e573241b614e2ec1371a0c2c76e8))
* Extend token expiration duration to 7 days ([22b47e4](https://www.github.com/asbjorjo/VeloTimer/commit/22b47e4c8617e0a344caac1e25a58c8abf558257))
* Generate OpenAPI specs at build ([d9cf32f](https://www.github.com/asbjorjo/VeloTimer/commit/d9cf32ff293a0d444385f190384e7774361770d1))
* Pass kc_action for application initiated actions ([6374fc2](https://www.github.com/asbjorjo/VeloTimer/commit/6374fc275448f2ddff5e5f4d14c5314a8761a56e))
* Try to add link for social providers on account ([8adda6a](https://www.github.com/asbjorjo/VeloTimer/commit/8adda6a79da004b5c3f239fecde049d411f8f80a))
* User Duende library for token handling ([c1f070e](https://www.github.com/asbjorjo/VeloTimer/commit/c1f070e5e42ed2e221eca49698980ed77450ce34))

### Bug Fixes

* Add azure messaging traces ([7ee0d16](https://www.github.com/asbjorjo/VeloTimer/commit/7ee0d1655ed96747cac6a8f7ae3e8512dd1f199c))
* Add DNS SRV service discovery ([1f86abe](https://www.github.com/asbjorjo/VeloTimer/commit/1f86abef685f5c8e68a4d7040d70ee0e7c56a822))
* Add explicit filter for otel loggin ([81ab2c4](https://www.github.com/asbjorjo/VeloTimer/commit/81ab2c482732de1a2f6b398742da8eef68499b33))
* Add forwarded headers from gateway ([bca0b8c](https://www.github.com/asbjorjo/VeloTimer/commit/bca0b8c6e6a3a5efe0578eda76cc11312e139485))
* Add generated API spec ([118e869](https://www.github.com/asbjorjo/VeloTimer/commit/118e8698fc3a671d6e1b2dc75c8f7f054a6e1a23))
* Add keycloak reference to services in apphost ([b5290e9](https://www.github.com/asbjorjo/VeloTimer/commit/b5290e94ad76153d6d77ce53820eca1557d92b96))
* Add logging to otlp ([c5dd92d](https://www.github.com/asbjorjo/VeloTimer/commit/c5dd92da5a67773cec861034ecc5452a78c7118d))
* Add package repository ([4f8a17f](https://www.github.com/asbjorjo/VeloTimer/commit/4f8a17fe30361335c1681640045f8162615ba860))
* Add suffic for dns srv service discovery ([ff15a1b](https://www.github.com/asbjorjo/VeloTimer/commit/ff15a1b147f1b4f6620b318def28259653068679))
* Add version to project ([fef4eab](https://www.github.com/asbjorjo/VeloTimer/commit/fef4eab2902c0337cc23f43d1bebfa0a1f086eef))
* Align package versions ([10d9694](https://www.github.com/asbjorjo/VeloTimer/commit/10d969425c845f4eaf04501982123edb2876a367))
* Align versions ([bec037f](https://www.github.com/asbjorjo/VeloTimer/commit/bec037f08f61d36179f77aa3f343d2ea7a5f8271))
* Apply https to development ([4c01816](https://www.github.com/asbjorjo/VeloTimer/commit/4c01816a3d06fd966bb2d089130c08f3ff12f542))
* Bump version ([dde23fb](https://www.github.com/asbjorjo/VeloTimer/commit/dde23fb9cb172ed264f11c8c30f46869147e08c4))
* Conflict ([98c3834](https://www.github.com/asbjorjo/VeloTimer/commit/98c383488aee3c230ac74352ffa73874729fe87c))
* Conflict ([f953577](https://www.github.com/asbjorjo/VeloTimer/commit/f95357761015e9dde5f8d636eef6cff77e9bba1b))
* Container image and do not build test as container ([c5b86a8](https://www.github.com/asbjorjo/VeloTimer/commit/c5b86a8a1b0c3a86f6c1ec48cabaf373c95fbe84))
* Correct URL for login/out ([e6ca92e](https://www.github.com/asbjorjo/VeloTimer/commit/e6ca92efb197962bb0456fdeb3beae8b74822b8e))
* Desperate times ([786b688](https://www.github.com/asbjorjo/VeloTimer/commit/786b6882ef8a2ccffe8365ad0b4e8c0972d4ba3e))
* Do not call addopentelemetry to register meters and sources ([c1f82d0](https://www.github.com/asbjorjo/VeloTimer/commit/c1f82d0d168bb69e56a28e7bec2b3c5de379f57e))
* Do not include API assets in client ([9239a0f](https://www.github.com/asbjorjo/VeloTimer/commit/9239a0f27f2e67717af72c50beea09099222e142))
* Do not use --include-symbols ([3450426](https://www.github.com/asbjorjo/VeloTimer/commit/3450426463e3b6aaf95bac5f2b922a1f486b7475))
* Drop ContainerRegistry from publish command ([e2c55f9](https://www.github.com/asbjorjo/VeloTimer/commit/e2c55f94ad21df4d8e1c96e65faeeaaebb37b138))
* Enable all sorts of endpoint resulotion ([cbd4e72](https://www.github.com/asbjorjo/VeloTimer/commit/cbd4e72dc422836afcb19346703822efd173994d))
* Force build ([2724ff7](https://www.github.com/asbjorjo/VeloTimer/commit/2724ff711d251cfd475dde4d0f3419ea787e918c))
* Force build ([017efe6](https://www.github.com/asbjorjo/VeloTimer/commit/017efe6f6b72485b98ce47b1d2917f24c76623be))
* Interceptor can go when instrumenting servicebus ([23e86d5](https://www.github.com/asbjorjo/VeloTimer/commit/23e86d5343cc234ce939d05aecbd0179c8d6e87f))
* Just force scheme to https ([d2b044b](https://www.github.com/asbjorjo/VeloTimer/commit/d2b044b2f1571d37b8bff0ae67588f09f5f6ab67))
* Just go back to defaults ([8d61cb7](https://www.github.com/asbjorjo/VeloTimer/commit/8d61cb7c1a6ad9aca8d87e923c95f48fda7c5006))
* Just use OpenAPI project reference.... ([b6e6189](https://www.github.com/asbjorjo/VeloTimer/commit/b6e6189bb82f7fb951fc716afcd8591c93cc31cf))
* Mockup some UI things ([c21346c](https://www.github.com/asbjorjo/VeloTimer/commit/c21346ca14981c9ae2e11a81f7fc4892469e9857))
* Move migration history to ef schema ([db3e856](https://www.github.com/asbjorjo/VeloTimer/commit/db3e856ba87f90c1d018e1639aad6911aca19141))
* Prefix service name with velotime ([82f87b9](https://www.github.com/asbjorjo/VeloTimer/commit/82f87b918ee44f8b82f96318c164affd7ceddbbd))
* Re-run migrations ([9ec6bf7](https://www.github.com/asbjorjo/VeloTimer/commit/9ec6bf74298e1c042bf62555b00ed15e64233f28))
* Remove adding otel exporter to logging ([3536d3b](https://www.github.com/asbjorjo/VeloTimer/commit/3536d3b23cd30d3e78c843fa45ce131f304664da))
* Remove DNS SRV service discovery ([a5788ff](https://www.github.com/asbjorjo/VeloTimer/commit/a5788ff47003cd18324c48942e34eb40553192e9))
* Remove explicit filter for otel logs ([26955df](https://www.github.com/asbjorjo/VeloTimer/commit/26955dfe23d5bb87b6d412faed31ae55018486f5))
* Remove unused import ([ed273af](https://www.github.com/asbjorjo/VeloTimer/commit/ed273aff6e4ee6cc3d8cda8797bb957805998b48))
* Required for something ([7c0812d](https://www.github.com/asbjorjo/VeloTimer/commit/7c0812dc43da5a6bbaceb63c8d9f145409fca37c))
* Required for something ([d2ccb94](https://www.github.com/asbjorjo/VeloTimer/commit/d2ccb94b375db59b9ef40d1128b9f3512aed8fd3))
* See if publish runs ([5fb53e4](https://www.github.com/asbjorjo/VeloTimer/commit/5fb53e4369ce331a999d5931580d20e677a616f1))
* Send token with API requests ([a832b05](https://www.github.com/asbjorjo/VeloTimer/commit/a832b0557c339cd8416c731d33932656b758d383))
* Set authority explicitly outside development ([7e2acfc](https://www.github.com/asbjorjo/VeloTimer/commit/7e2acfc01a3714c2fd63f4867977a5aaa833f328))
* Set container image names ([88b9803](https://www.github.com/asbjorjo/VeloTimer/commit/88b9803c570167f8b75f6ff9d47e9675f5f29988))
* Switch all to aspnet base image ([cf9b6da](https://www.github.com/asbjorjo/VeloTimer/commit/cf9b6da3b7f573965c113327ea4babc9ed198f83))
* Tag all images with version ([b223691](https://www.github.com/asbjorjo/VeloTimer/commit/b22369105bd1fbb1d9f2e6bd23d70042326b1492))
* Token endpoint ([510aa67](https://www.github.com/asbjorjo/VeloTimer/commit/510aa67bae7158e0b013e0037df88aa8e4460b7b))
* Try explicit config of log export ([369bd1d](https://www.github.com/asbjorjo/VeloTimer/commit/369bd1d99de22dd8725b19bc125c5335db5d7993))
* Try to change order of otel registrations ([0ca8c25](https://www.github.com/asbjorjo/VeloTimer/commit/0ca8c253367af2941f4e6bec07bb827660f708b8))
* Update container base image to use alpine variant ([22d19cb](https://www.github.com/asbjorjo/VeloTimer/commit/22d19cbf98722e26e7cc567fcacf1fe08392856d))
* Update container base image to use alpine variant ([e37d678](https://www.github.com/asbjorjo/VeloTimer/commit/e37d6784c709d55d8e31194eb72157ab42e1378b))
* Update project file for container configuration ([9589900](https://www.github.com/asbjorjo/VeloTimer/commit/9589900a276a9929d7736d6e6de2527cb9f270c6))
* Update project file for container settings and globalization ([ba2c5a4](https://www.github.com/asbjorjo/VeloTimer/commit/ba2c5a4569913873467a324062e2acef706a7b9a))
* Use duende things, not keycloak ([6e4aef7](https://www.github.com/asbjorjo/VeloTimer/commit/6e4aef781533e0eba7c3fc4103671dcc12fc3075))
* Use http for internal services ([fe1854c](https://www.github.com/asbjorjo/VeloTimer/commit/fe1854cd62acc338daf69faefb5d3a4721ed60ef))
* Use snake case at runtime ([a15c2d1](https://www.github.com/asbjorjo/VeloTimer/commit/a15c2d1413b1f2a2ff063de53d1b64622c7ef7c8))
* Where to push images ([2701ebe](https://www.github.com/asbjorjo/VeloTimer/commit/2701ebe24c94b297451e9bbd5e478d30b44bcb65))
* Wrong case in filename ([06cccf3](https://www.github.com/asbjorjo/VeloTimer/commit/06cccf33539b110350b3ab6697c3ca9315936ed6))

<a name="3.0.0-alpha.75"></a>
## [3.0.0-alpha.75](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.75) (2026-10-29)

### Bug Fixes

* **statistics:** authorize profile writes with admin permissions ([cb6caa3](https://www.github.com/asbjorjo/VeloTimer/commit/cb6caa3ec7b320d4ac609b6bda779f99b652a56f))

<a name="3.0.0-alpha.74"></a>
## [3.0.0-alpha.74](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.74) (2026-10-29)

### Features

* **statistics:** add public best-results query ([195fa7d](https://www.github.com/asbjorjo/VeloTimer/commit/195fa7d303655c2d76da7690e9310ad3fd624dbb))
* **web:** display reusable best-times table on home page ([d590fdd](https://www.github.com/asbjorjo/VeloTimer/commit/d590fddf010ced974d596fca5426f7ee104adc3f))
* **web:** expose best results through statistics services ([3e0208f](https://www.github.com/asbjorjo/VeloTimer/commit/3e0208f180dcc3a277887a52b9adec379f05d170))

### Bug Fixes

* **helm:** configure migration API service discovery ([faecd5a](https://www.github.com/asbjorjo/VeloTimer/commit/faecd5ac321d6eb32e25b81ec06bf22209425db3))

<a name="3.0.0-alpha.73"></a>
## [3.0.0-alpha.73](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.73) (2026-10-29)

### Bug Fixes

* **timing:** declare no-content mutation responses in OpenAPI ([5fc1b9f](https://www.github.com/asbjorjo/VeloTimer/commit/5fc1b9f1021b7b906fcf03c4045168a79d8e2296))
* **web:** accept all 2xx statuses for no-body upstream mutations ([8636b11](https://www.github.com/asbjorjo/VeloTimer/commit/8636b1124cb9ec43bb436c6aba1b70be1d38daf9))

<a name="3.0.0-alpha.72"></a>
## [3.0.0-alpha.72](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.72) (2026-10-29)

### Features

* **helm:** add global OIDC authority configuration ([7bb5c0a](https://www.github.com/asbjorjo/VeloTimer/commit/7bb5c0a168fa46bc756739ba3190a92e900aa937))
* **statistics:** add scoped transponder owner profile lookups ([6640136](https://www.github.com/asbjorjo/VeloTimer/commit/664013651ed1b7d1fee88f5953af95e60bac339c))
* **timing:** support dated transponder ownership registration ([55ce5f6](https://www.github.com/asbjorjo/VeloTimer/commit/55ce5f6fefde3df8fa28b75fd799dcb874e51f46))
* **web:** add local registration dates to transponder forms ([9b05a8e](https://www.github.com/asbjorjo/VeloTimer/commit/9b05a8e40a2999537c91aca66c0b228db6fd3574))
* **web:** forward dated registration and ownership confirmation ([f43dd6a](https://www.github.com/asbjorjo/VeloTimer/commit/f43dd6a13cdaace0ca3a9104773f31ba6d1e0319))
* **web:** resolve transponder owners through a read-only directory ([5b6bb8c](https://www.github.com/asbjorjo/VeloTimer/commit/5b6bb8c041c4e6fad36d92f2d1c9a0f387d5ae4f))
* **web:** show named transponder owners and searchable selection ([fb0c89e](https://www.github.com/asbjorjo/VeloTimer/commit/fb0c89edfc18dc26f811a9a8b67ee605e472ac69))

### Bug Fixes

* Update role mapping and default role ([0a295f4](https://www.github.com/asbjorjo/VeloTimer/commit/0a295f49b5f34d14700e392b89ac342ffec3fce2))

<a name="3.0.0-alpha.71"></a>
## [3.0.0-alpha.71](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.71) (2026-10-27)

### Features

* Stop requesting velotime scope ([49bb46b](https://www.github.com/asbjorjo/VeloTimer/commit/49bb46b629b4ffa897bf755e3d54ec413be67b2b))

<a name="3.0.0-alpha.70"></a>
## [3.0.0-alpha.70](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.70) (2026-10-27)

### Bug Fixes

* **telemetry:** avoid OTLP exporter conflicts with auto instrumentation ([80b68cb](https://www.github.com/asbjorjo/VeloTimer/commit/80b68cb0c3d8316b7160ab91214e78aa9d598e1a))

<a name="3.0.0-alpha.69"></a>
## [3.0.0-alpha.69](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.69) (2026-10-26)

### Bug Fixes

* Do not required OIDC AUDIENCE when building API spec ([102c46a](https://www.github.com/asbjorjo/VeloTimer/commit/102c46af7bea98f5679d1122aa3e7e0cd22cb3c2))
* Remove unused link call ([54066ea](https://www.github.com/asbjorjo/VeloTimer/commit/54066ea0d387213c2eb64629065997fc3bd937b1))

<a name="3.0.0-alpha.68"></a>
## [3.0.0-alpha.68](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.68) (2026-10-26)

### Features

* **charts:** support OIDC_AUDIENCE configuration for module api charts ([d464889](https://www.github.com/asbjorjo/VeloTimer/commit/d464889894d870eb147800908a4285df69f457d9))

### Bug Fixes

* **chart:** Fix line breaks ([4542380](https://www.github.com/asbjorjo/VeloTimer/commit/4542380c66c3589627b9b8205ae874a9e15ffdb0))

<a name="3.0.0-alpha.67"></a>
## [3.0.0-alpha.67](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.67) (2026-10-26)

### Features

* Application helm chart ([6429009](https://www.github.com/asbjorjo/VeloTimer/commit/64290095d3386be6298e6c992abc87cde079d150))
* **auth:** configure OIDC audience using application base URL ([963c459](https://www.github.com/asbjorjo/VeloTimer/commit/963c459e3b776b4d6fc8b25d4b978db182f001e1))

<a name="3.0.0-alpha.66"></a>
## [3.0.0-alpha.66](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.66) (2026-10-26)

### Features

* **webui:** add development-only auth debug page ([d6b03be](https://www.github.com/asbjorjo/VeloTimer/commit/d6b03be488bc8b9c18dc536ef087589157788554))

<a name="3.0.0-alpha.65"></a>
## [3.0.0-alpha.65](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.65) (2026-10-25)

### Features

* **auth:** authorize administration with permission claims ([adc19bf](https://www.github.com/asbjorjo/VeloTimer/commit/adc19bf335acef8bfdbeff77ead63272afbda334))
* **facilities:** manage facilities and course layouts ([d7974c7](https://www.github.com/asbjorjo/VeloTimer/commit/d7974c73764c87cdd45628be3527a2241d791086))
* **statistics:** manage items and configurations ([1c3f509](https://www.github.com/asbjorjo/VeloTimer/commit/1c3f509b2322d1794db3fdc91e4731c41394f178))
* **timing:** manage installations and transponder ownership ([f9f5d06](https://www.github.com/asbjorjo/VeloTimer/commit/f9f5d06ea71e80ea5ac814e82e4dca1bf587848f))
* **webui:** replace administration pages with policy-driven workflows ([d0e7c39](https://www.github.com/asbjorjo/VeloTimer/commit/d0e7c39a5d87d7d1d80c8d6ac9999300367e4667))

### Bug Fixes

* **auth:** map Keycloak realm roles from UserInfo ([09c86cc](https://www.github.com/asbjorjo/VeloTimer/commit/09c86cce80d030c1fc9804c5f0393e30d36479fb))

<a name="3.0.0-alpha.64"></a>
## [3.0.0-alpha.64](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.64) (2026-10-23)

### Features

* **webui:** share data protection keys and auth state across replicas ([7f12cd5](https://www.github.com/asbjorjo/VeloTimer/commit/7f12cd591fcde12f17ac8f8112dace6de0c346f2))

<a name="3.0.0-alpha.63"></a>
## [3.0.0-alpha.63](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.63) (2026-10-23)

### Features

* **webui:** implement backend-for-frontend pattern ([b5f4226](https://www.github.com/asbjorjo/VeloTimer/commit/b5f42266689d69ff29a193a01585686b64cf5d89))

### Bug Fixes

* webui client should not be published by itself ([b17f6b3](https://www.github.com/asbjorjo/VeloTimer/commit/b17f6b352348589ffc1f913c0a62c79b6a42136e))

<a name="3.0.0-alpha.62"></a>
## [3.0.0-alpha.62](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.62) (2026-10-19)

### Features

* Add basic devcontainer support ([ced957f](https://www.github.com/asbjorjo/VeloTimer/commit/ced957f50bdc888bb14690ef407eff7dd6a941bf))
* **timing:** add transponder ownership surrogate key ([32a6e08](https://www.github.com/asbjorjo/VeloTimer/commit/32a6e080ed80c8dcd9f42eac51e26869ffc6c3a4))

### Bug Fixes

* Move user migration to non-alpine ([bf2f1ba](https://www.github.com/asbjorjo/VeloTimer/commit/bf2f1ba5cb2627ca2e26304265a47c36cdfe6a38))

<a name="3.0.0-alpha.61"></a>
## [3.0.0-alpha.61](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.61) (2026-10-19)

### Features

* Add authentication to modules ([89b9df6](https://www.github.com/asbjorjo/VeloTimer/commit/89b9df66b7d9f1bb5ab2a012e03a728ebea1cc7d))
* Add basic functionality to modules for timing, statistics and facility setup ([ff5ec1f](https://www.github.com/asbjorjo/VeloTimer/commit/ff5ec1f5c9471c39e5b468cf3ebb04572ad493ab))
* Add client for keycloak account API ([9525e8f](https://www.github.com/asbjorjo/VeloTimer/commit/9525e8ff1b4214b67a6949537af290d297371b48))
* Add docker support to api host ([9117bd2](https://www.github.com/asbjorjo/VeloTimer/commit/9117bd247c9ab88883712320031edc84b89c913d))
* Add installation admin ([ae50882](https://www.github.com/asbjorjo/VeloTimer/commit/ae50882ca56540a5de09bbc24bd035ad155c6aec))
* Add service for migrating users from ([6f7cf5c](https://www.github.com/asbjorjo/VeloTimer/commit/6f7cf5c3aea54a0c47b8764de8d688f9763f028b))
* Add trace for loop status ([3552b9e](https://www.github.com/asbjorjo/VeloTimer/commit/3552b9e3aa6b6d28ba69823e7cca29d09dae3340))
* Allow unlinking providers ([ae38565](https://www.github.com/asbjorjo/VeloTimer/commit/ae38565f05a36240904b3e42b7aa557daff130da))
* Auto-generate all clients on build ([c889c91](https://www.github.com/asbjorjo/VeloTimer/commit/c889c91501b3687031ac7a9497f3aa9f02de11e6))
* Auto-generate API client ([abcd444](https://www.github.com/asbjorjo/VeloTimer/commit/abcd44443f08e573241b614e2ec1371a0c2c76e8))
* Extend token expiration duration to 7 days ([22b47e4](https://www.github.com/asbjorjo/VeloTimer/commit/22b47e4c8617e0a344caac1e25a58c8abf558257))
* Force build ([b145d6c](https://www.github.com/asbjorjo/VeloTimer/commit/b145d6cda65e15d2d0e8b2048443d7bbebbca3ff))
* Force rebuild ([2dc05bd](https://www.github.com/asbjorjo/VeloTimer/commit/2dc05bdbf7eadf131596eecf17dc0920a3259819))
* Force version ([024d3ab](https://www.github.com/asbjorjo/VeloTimer/commit/024d3ab46bc4346059e8c86a9c5e712c3e9bfe96))
* Generate OpenAPI specs at build ([d9cf32f](https://www.github.com/asbjorjo/VeloTimer/commit/d9cf32ff293a0d444385f190384e7774361770d1))
* Pass kc_action for application initiated actions ([6374fc2](https://www.github.com/asbjorjo/VeloTimer/commit/6374fc275448f2ddff5e5f4d14c5314a8761a56e))
* Show as offline if not seen last five minutes ([01323ab](https://www.github.com/asbjorjo/VeloTimer/commit/01323abc480a3ee13a4dc18028e3bcbda5437d17))
* Try to add link for social providers on account ([8adda6a](https://www.github.com/asbjorjo/VeloTimer/commit/8adda6a79da004b5c3f239fecde049d411f8f80a))
* Update installation last seen on loop status ([fc24a8d](https://www.github.com/asbjorjo/VeloTimer/commit/fc24a8d0264da29e5bd09ca6dd429764f589d469))
* User Duende library for token handling ([c1f070e](https://www.github.com/asbjorjo/VeloTimer/commit/c1f070e5e42ed2e221eca49698980ed77450ce34))
* **statistics:** add stats profile management API ([bfe7257](https://www.github.com/asbjorjo/VeloTimer/commit/bfe7257905674de81915896a15ddd5137581b7b1))
* **timing:** add transponder ownership endpoints ([b9fcc91](https://www.github.com/asbjorjo/VeloTimer/commit/b9fcc91c9d3f81813a42800425458a2efa3ef2e8))
* **users:** migrate profiles and transponder ownership ([b2cc3c7](https://www.github.com/asbjorjo/VeloTimer/commit/b2cc3c7560a16d5308e8a7ad4b2b719129cf06de))

### Bug Fixes

* Add azure messaging traces ([7ee0d16](https://www.github.com/asbjorjo/VeloTimer/commit/7ee0d1655ed96747cac6a8f7ae3e8512dd1f199c))
* Add DNS SRV service discovery ([1f86abe](https://www.github.com/asbjorjo/VeloTimer/commit/1f86abef685f5c8e68a4d7040d70ee0e7c56a822))
* Add explicit filter for otel loggin ([81ab2c4](https://www.github.com/asbjorjo/VeloTimer/commit/81ab2c482732de1a2f6b398742da8eef68499b33))
* Add forwarded headers from gateway ([bca0b8c](https://www.github.com/asbjorjo/VeloTimer/commit/bca0b8c6e6a3a5efe0578eda76cc11312e139485))
* Add generated API spec ([118e869](https://www.github.com/asbjorjo/VeloTimer/commit/118e8698fc3a671d6e1b2dc75c8f7f054a6e1a23))
* Add keycloak reference to services in apphost ([b5290e9](https://www.github.com/asbjorjo/VeloTimer/commit/b5290e94ad76153d6d77ce53820eca1557d92b96))
* Add logging to otlp ([c5dd92d](https://www.github.com/asbjorjo/VeloTimer/commit/c5dd92da5a67773cec861034ecc5452a78c7118d))
* Add package repository ([4f8a17f](https://www.github.com/asbjorjo/VeloTimer/commit/4f8a17fe30361335c1681640045f8162615ba860))
* Add suffic for dns srv service discovery ([ff15a1b](https://www.github.com/asbjorjo/VeloTimer/commit/ff15a1b147f1b4f6620b318def28259653068679))
* Add version to project ([fef4eab](https://www.github.com/asbjorjo/VeloTimer/commit/fef4eab2902c0337cc23f43d1bebfa0a1f086eef))
* Align package versions ([10d9694](https://www.github.com/asbjorjo/VeloTimer/commit/10d969425c845f4eaf04501982123edb2876a367))
* Align versions ([bec037f](https://www.github.com/asbjorjo/VeloTimer/commit/bec037f08f61d36179f77aa3f343d2ea7a5f8271))
* Apply https to development ([4c01816](https://www.github.com/asbjorjo/VeloTimer/commit/4c01816a3d06fd966bb2d089130c08f3ff12f542))
* Bump version ([dde23fb](https://www.github.com/asbjorjo/VeloTimer/commit/dde23fb9cb172ed264f11c8c30f46869147e08c4))
* Check if empty ([6034753](https://www.github.com/asbjorjo/VeloTimer/commit/603475376eec007c375d856fbf678b557485561c))
* Check value not null or empty ([d3b4946](https://www.github.com/asbjorjo/VeloTimer/commit/d3b49463ac4093166b10f097eba68fb6a364dd42))
* Conflict ([98c3834](https://www.github.com/asbjorjo/VeloTimer/commit/98c383488aee3c230ac74352ffa73874729fe87c))
* Conflict ([f953577](https://www.github.com/asbjorjo/VeloTimer/commit/f95357761015e9dde5f8d636eef6cff77e9bba1b))
* Container image and do not build test as container ([c5b86a8](https://www.github.com/asbjorjo/VeloTimer/commit/c5b86a8a1b0c3a86f6c1ec48cabaf373c95fbe84))
* Convert times to UTC before saving ([343bd17](https://www.github.com/asbjorjo/VeloTimer/commit/343bd176f81d4aab201b7b0289db601773b28b35))
* Correct URL for login/out ([e6ca92e](https://www.github.com/asbjorjo/VeloTimer/commit/e6ca92efb197962bb0456fdeb3beae8b74822b8e))
* Count laps for Christmas event on Red pursuit ([#100](https://www.github.com/asbjorjo/VeloTimer/issues/100)) ([52a721b](https://www.github.com/asbjorjo/VeloTimer/commit/52a721b70cc6b6dc191f39ab07e33d263655ee93))
* Desperate times ([786b688](https://www.github.com/asbjorjo/VeloTimer/commit/786b6882ef8a2ccffe8365ad0b4e8c0972d4ba3e))
* Do not call addopentelemetry to register meters and sources ([c1f82d0](https://www.github.com/asbjorjo/VeloTimer/commit/c1f82d0d168bb69e56a28e7bec2b3c5de379f57e))
* Do not include API assets in client ([9239a0f](https://www.github.com/asbjorjo/VeloTimer/commit/9239a0f27f2e67717af72c50beea09099222e142))
* Do not update last seen to earlier time ([0d30558](https://www.github.com/asbjorjo/VeloTimer/commit/0d30558bd0dd596195b06821fbbeb3256afe53b3))
* Do not use --include-symbols ([3450426](https://www.github.com/asbjorjo/VeloTimer/commit/3450426463e3b6aaf95bac5f2b922a1f486b7475))
* Drop ContainerRegistry from publish command ([e2c55f9](https://www.github.com/asbjorjo/VeloTimer/commit/e2c55f94ad21df4d8e1c96e65faeeaaebb37b138))
* Enable all sorts of endpoint resulotion ([cbd4e72](https://www.github.com/asbjorjo/VeloTimer/commit/cbd4e72dc422836afcb19346703822efd173994d))
* Expose installation last seen time in API ([fdbc09e](https://www.github.com/asbjorjo/VeloTimer/commit/fdbc09e22cb3db14f26e7927b543ee91f9853355))
* Force build ([2724ff7](https://www.github.com/asbjorjo/VeloTimer/commit/2724ff711d251cfd475dde4d0f3419ea787e918c))
* Force build ([017efe6](https://www.github.com/asbjorjo/VeloTimer/commit/017efe6f6b72485b98ce47b1d2917f24c76623be))
* Forgot the case of eternal ownership ([dedd55a](https://www.github.com/asbjorjo/VeloTimer/commit/dedd55a56b36f972f0426d5e1e22bf9cfa315ba4))
* Interceptor can go when instrumenting servicebus ([23e86d5](https://www.github.com/asbjorjo/VeloTimer/commit/23e86d5343cc234ce939d05aecbd0179c8d6e87f))
* Just force scheme to https ([d2b044b](https://www.github.com/asbjorjo/VeloTimer/commit/d2b044b2f1571d37b8bff0ae67588f09f5f6ab67))
* Just go back to defaults ([8d61cb7](https://www.github.com/asbjorjo/VeloTimer/commit/8d61cb7c1a6ad9aca8d87e923c95f48fda7c5006))
* Just use OpenAPI project reference.... ([b6e6189](https://www.github.com/asbjorjo/VeloTimer/commit/b6e6189bb82f7fb951fc716afcd8591c93cc31cf))
* Mockup some UI things ([c21346c](https://www.github.com/asbjorjo/VeloTimer/commit/c21346ca14981c9ae2e11a81f7fc4892469e9857))
* Move conversions to Label field ([b76ea8b](https://www.github.com/asbjorjo/VeloTimer/commit/b76ea8b9c73a8a14d8ddefa6182e002cbe8d4704))
* Move migration history to ef schema ([db3e856](https://www.github.com/asbjorjo/VeloTimer/commit/db3e856ba87f90c1d018e1639aad6911aca19141))
* Prefer connection string for keycloak, fallback only for development ([c2e24d6](https://www.github.com/asbjorjo/VeloTimer/commit/c2e24d6e7ecb0a8c10d4008d8223780f3f612aca))
* Prefix service name with velotime ([82f87b9](https://www.github.com/asbjorjo/VeloTimer/commit/82f87b918ee44f8b82f96318c164affd7ceddbbd))
* Re-run migrations ([9ec6bf7](https://www.github.com/asbjorjo/VeloTimer/commit/9ec6bf74298e1c042bf62555b00ed15e64233f28))
* Remove adding otel exporter to logging ([3536d3b](https://www.github.com/asbjorjo/VeloTimer/commit/3536d3b23cd30d3e78c843fa45ce131f304664da))
* Remove DNS SRV service discovery ([a5788ff](https://www.github.com/asbjorjo/VeloTimer/commit/a5788ff47003cd18324c48942e34eb40553192e9))
* Remove explicit filter for otel logs ([26955df](https://www.github.com/asbjorjo/VeloTimer/commit/26955dfe23d5bb87b6d412faed31ae55018486f5))
* Remove unused import ([ed273af](https://www.github.com/asbjorjo/VeloTimer/commit/ed273aff6e4ee6cc3d8cda8797bb957805998b48))
* Required for something ([7c0812d](https://www.github.com/asbjorjo/VeloTimer/commit/7c0812dc43da5a6bbaceb63c8d9f145409fca37c))
* Required for something ([d2ccb94](https://www.github.com/asbjorjo/VeloTimer/commit/d2ccb94b375db59b9ef40d1128b9f3512aed8fd3))
* See if publish runs ([5fb53e4](https://www.github.com/asbjorjo/VeloTimer/commit/5fb53e4369ce331a999d5931580d20e677a616f1))
* Send token with API requests ([a832b05](https://www.github.com/asbjorjo/VeloTimer/commit/a832b0557c339cd8416c731d33932656b758d383))
* Set authority explicitly outside development ([7e2acfc](https://www.github.com/asbjorjo/VeloTimer/commit/7e2acfc01a3714c2fd63f4867977a5aaa833f328))
* Set container image names ([88b9803](https://www.github.com/asbjorjo/VeloTimer/commit/88b9803c570167f8b75f6ff9d47e9675f5f29988))
* Switch all to aspnet base image ([cf9b6da](https://www.github.com/asbjorjo/VeloTimer/commit/cf9b6da3b7f573965c113327ea4babc9ed198f83))
* Tag all images with version ([b223691](https://www.github.com/asbjorjo/VeloTimer/commit/b22369105bd1fbb1d9f2e6bd23d70042326b1492))
* Token endpoint ([510aa67](https://www.github.com/asbjorjo/VeloTimer/commit/510aa67bae7158e0b013e0037df88aa8e4460b7b))
* Try explicit config of log export ([369bd1d](https://www.github.com/asbjorjo/VeloTimer/commit/369bd1d99de22dd8725b19bc125c5335db5d7993))
* Try to change order of otel registrations ([0ca8c25](https://www.github.com/asbjorjo/VeloTimer/commit/0ca8c253367af2941f4e6bec07bb827660f708b8))
* Update container base image to use alpine variant ([22d19cb](https://www.github.com/asbjorjo/VeloTimer/commit/22d19cbf98722e26e7cc567fcacf1fe08392856d))
* Update container base image to use alpine variant ([e37d678](https://www.github.com/asbjorjo/VeloTimer/commit/e37d6784c709d55d8e31194eb72157ab42e1378b))
* Update project file for container configuration ([9589900](https://www.github.com/asbjorjo/VeloTimer/commit/9589900a276a9929d7736d6e6de2527cb9f270c6))
* Update project file for container settings and globalization ([ba2c5a4](https://www.github.com/asbjorjo/VeloTimer/commit/ba2c5a4569913873467a324062e2acef706a7b9a))
* Use ([986cfea](https://www.github.com/asbjorjo/VeloTimer/commit/986cfea9d41583db70452bdcfddba12840322fe2))
* Use duende things, not keycloak ([6e4aef7](https://www.github.com/asbjorjo/VeloTimer/commit/6e4aef781533e0eba7c3fc4103671dcc12fc3075))
* Use http for internal services ([fe1854c](https://www.github.com/asbjorjo/VeloTimer/commit/fe1854cd62acc338daf69faefb5d3a4721ed60ef))
* Use parameterless constructor ([4986256](https://www.github.com/asbjorjo/VeloTimer/commit/498625638d7186c0f3ad855cb1c2e8916f08e8a8))
* Use snake case at runtime ([a15c2d1](https://www.github.com/asbjorjo/VeloTimer/commit/a15c2d1413b1f2a2ff063de53d1b64622c7ef7c8))
* Where to push images ([2701ebe](https://www.github.com/asbjorjo/VeloTimer/commit/2701ebe24c94b297451e9bbd5e478d30b44bcb65))
* Wrong case in filename ([06cccf3](https://www.github.com/asbjorjo/VeloTimer/commit/06cccf33539b110350b3ab6697c3ca9315936ed6))
* **api:** handle facility distance and statistics edge cases ([22a9380](https://www.github.com/asbjorjo/VeloTimer/commit/22a93808a39983113b1cc970663f2039c1aed642))
* **auth:** configure Keycloak token validation and scopes ([a5d79ce](https://www.github.com/asbjorjo/VeloTimer/commit/a5d79ceefba42e113ce83941e8e096360123edc2))
* **timing:** handle duplicate passings and transponder updates ([6e5a9c5](https://www.github.com/asbjorjo/VeloTimer/commit/6e5a9c501877b51546c95191f57812283a686e45))

<a name="3.0.0-alpha.59"></a>
## [3.0.0-alpha.59](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.59) (2026-06-03)

### Features

* Force version ([024d3ab](https://www.github.com/asbjorjo/VeloTimer/commit/024d3ab46bc4346059e8c86a9c5e712c3e9bfe96))

<a name="3.0.0-alpha.58"></a>
## [3.0.0-alpha.58](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.58) (2026-06-03)

### Features

* Force build ([b145d6c](https://www.github.com/asbjorjo/VeloTimer/commit/b145d6cda65e15d2d0e8b2048443d7bbebbca3ff))
* Force rebuild ([2dc05bd](https://www.github.com/asbjorjo/VeloTimer/commit/2dc05bdbf7eadf131596eecf17dc0920a3259819))

<a name="3.0.0-alpha.57"></a>
## [3.0.0-alpha.57](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.57) (2026-06-03)

### Features

* Add trace for loop status ([3552b9e](https://www.github.com/asbjorjo/VeloTimer/commit/3552b9e3aa6b6d28ba69823e7cca29d09dae3340))

<a name="3.0.0-alpha.56"></a>
## [3.0.0-alpha.56](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.56) (2026-06-03)

### Features

* Show as offline if not seen last five minutes ([01323ab](https://www.github.com/asbjorjo/VeloTimer/commit/01323abc480a3ee13a4dc18028e3bcbda5437d17))
* Update installation last seen on loop status ([fc24a8d](https://www.github.com/asbjorjo/VeloTimer/commit/fc24a8d0264da29e5bd09ca6dd429764f589d469))

<a name="3.0.0-alpha.55"></a>
## [3.0.0-alpha.55](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.55) (2026-06-03)

### Bug Fixes

* Expose installation last seen time in API ([fdbc09e](https://www.github.com/asbjorjo/VeloTimer/commit/fdbc09e22cb3db14f26e7927b543ee91f9853355))
* Use ([986cfea](https://www.github.com/asbjorjo/VeloTimer/commit/986cfea9d41583db70452bdcfddba12840322fe2))

<a name="3.0.0-alpha.54"></a>
## [3.0.0-alpha.54](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.54) (2026-06-01)

### Bug Fixes

* Check value not null or empty ([d3b4946](https://www.github.com/asbjorjo/VeloTimer/commit/d3b49463ac4093166b10f097eba68fb6a364dd42))
* Move conversions to Label field ([b76ea8b](https://www.github.com/asbjorjo/VeloTimer/commit/b76ea8b9c73a8a14d8ddefa6182e002cbe8d4704))

<a name="3.0.0-alpha.53"></a>
## [3.0.0-alpha.53](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.53) (2026-06-01)

### Bug Fixes

* Check if empty ([6034753](https://www.github.com/asbjorjo/VeloTimer/commit/603475376eec007c375d856fbf678b557485561c))

<a name="3.0.0-alpha.52"></a>
## [3.0.0-alpha.52](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.52) (2026-06-01)

### Bug Fixes

* Use parameterless constructor ([4986256](https://www.github.com/asbjorjo/VeloTimer/commit/498625638d7186c0f3ad855cb1c2e8916f08e8a8))

<a name="3.0.0-alpha.51"></a>
## [3.0.0-alpha.51](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.51) (2026-05-31)

### Features

* Add authentication to modules ([89b9df6](https://www.github.com/asbjorjo/VeloTimer/commit/89b9df66b7d9f1bb5ab2a012e03a728ebea1cc7d))
* Add basic functionality to modules for timing, statistics and facility setup ([ff5ec1f](https://www.github.com/asbjorjo/VeloTimer/commit/ff5ec1f5c9471c39e5b468cf3ebb04572ad493ab))
* Add client for keycloak account API ([9525e8f](https://www.github.com/asbjorjo/VeloTimer/commit/9525e8ff1b4214b67a6949537af290d297371b48))
* Add docker support to api host ([9117bd2](https://www.github.com/asbjorjo/VeloTimer/commit/9117bd247c9ab88883712320031edc84b89c913d))
* Add installation admin ([ae50882](https://www.github.com/asbjorjo/VeloTimer/commit/ae50882ca56540a5de09bbc24bd035ad155c6aec))
* Allow unlinking providers ([ae38565](https://www.github.com/asbjorjo/VeloTimer/commit/ae38565f05a36240904b3e42b7aa557daff130da))
* Auto-generate all clients on build ([c889c91](https://www.github.com/asbjorjo/VeloTimer/commit/c889c91501b3687031ac7a9497f3aa9f02de11e6))
* Auto-generate API client ([abcd444](https://www.github.com/asbjorjo/VeloTimer/commit/abcd44443f08e573241b614e2ec1371a0c2c76e8))
* Extend token expiration duration to 7 days ([22b47e4](https://www.github.com/asbjorjo/VeloTimer/commit/22b47e4c8617e0a344caac1e25a58c8abf558257))
* Generate OpenAPI specs at build ([d9cf32f](https://www.github.com/asbjorjo/VeloTimer/commit/d9cf32ff293a0d444385f190384e7774361770d1))
* Pass kc_action for application initiated actions ([6374fc2](https://www.github.com/asbjorjo/VeloTimer/commit/6374fc275448f2ddff5e5f4d14c5314a8761a56e))
* Try to add link for social providers on account ([8adda6a](https://www.github.com/asbjorjo/VeloTimer/commit/8adda6a79da004b5c3f239fecde049d411f8f80a))
* User Duende library for token handling ([c1f070e](https://www.github.com/asbjorjo/VeloTimer/commit/c1f070e5e42ed2e221eca49698980ed77450ce34))

### Bug Fixes

* Add azure messaging traces ([7ee0d16](https://www.github.com/asbjorjo/VeloTimer/commit/7ee0d1655ed96747cac6a8f7ae3e8512dd1f199c))
* Add DNS SRV service discovery ([1f86abe](https://www.github.com/asbjorjo/VeloTimer/commit/1f86abef685f5c8e68a4d7040d70ee0e7c56a822))
* Add explicit filter for otel loggin ([81ab2c4](https://www.github.com/asbjorjo/VeloTimer/commit/81ab2c482732de1a2f6b398742da8eef68499b33))
* Add forwarded headers from gateway ([bca0b8c](https://www.github.com/asbjorjo/VeloTimer/commit/bca0b8c6e6a3a5efe0578eda76cc11312e139485))
* Add generated API spec ([118e869](https://www.github.com/asbjorjo/VeloTimer/commit/118e8698fc3a671d6e1b2dc75c8f7f054a6e1a23))
* Add keycloak reference to services in apphost ([b5290e9](https://www.github.com/asbjorjo/VeloTimer/commit/b5290e94ad76153d6d77ce53820eca1557d92b96))
* Add logging to otlp ([c5dd92d](https://www.github.com/asbjorjo/VeloTimer/commit/c5dd92da5a67773cec861034ecc5452a78c7118d))
* Add package repository ([4f8a17f](https://www.github.com/asbjorjo/VeloTimer/commit/4f8a17fe30361335c1681640045f8162615ba860))
* Add suffic for dns srv service discovery ([ff15a1b](https://www.github.com/asbjorjo/VeloTimer/commit/ff15a1b147f1b4f6620b318def28259653068679))
* Add version to project ([fef4eab](https://www.github.com/asbjorjo/VeloTimer/commit/fef4eab2902c0337cc23f43d1bebfa0a1f086eef))
* Align package versions ([10d9694](https://www.github.com/asbjorjo/VeloTimer/commit/10d969425c845f4eaf04501982123edb2876a367))
* Align versions ([bec037f](https://www.github.com/asbjorjo/VeloTimer/commit/bec037f08f61d36179f77aa3f343d2ea7a5f8271))
* Apply https to development ([4c01816](https://www.github.com/asbjorjo/VeloTimer/commit/4c01816a3d06fd966bb2d089130c08f3ff12f542))
* Bump version ([dde23fb](https://www.github.com/asbjorjo/VeloTimer/commit/dde23fb9cb172ed264f11c8c30f46869147e08c4))
* Conflict ([98c3834](https://www.github.com/asbjorjo/VeloTimer/commit/98c383488aee3c230ac74352ffa73874729fe87c))
* Conflict ([f953577](https://www.github.com/asbjorjo/VeloTimer/commit/f95357761015e9dde5f8d636eef6cff77e9bba1b))
* Container image and do not build test as container ([c5b86a8](https://www.github.com/asbjorjo/VeloTimer/commit/c5b86a8a1b0c3a86f6c1ec48cabaf373c95fbe84))
* Correct URL for login/out ([e6ca92e](https://www.github.com/asbjorjo/VeloTimer/commit/e6ca92efb197962bb0456fdeb3beae8b74822b8e))
* Count laps for Christmas event on Red pursuit ([#100](https://www.github.com/asbjorjo/VeloTimer/issues/100)) ([52a721b](https://www.github.com/asbjorjo/VeloTimer/commit/52a721b70cc6b6dc191f39ab07e33d263655ee93))
* Desperate times ([786b688](https://www.github.com/asbjorjo/VeloTimer/commit/786b6882ef8a2ccffe8365ad0b4e8c0972d4ba3e))
* Do not call addopentelemetry to register meters and sources ([c1f82d0](https://www.github.com/asbjorjo/VeloTimer/commit/c1f82d0d168bb69e56a28e7bec2b3c5de379f57e))
* Do not include API assets in client ([9239a0f](https://www.github.com/asbjorjo/VeloTimer/commit/9239a0f27f2e67717af72c50beea09099222e142))
* Do not use --include-symbols ([3450426](https://www.github.com/asbjorjo/VeloTimer/commit/3450426463e3b6aaf95bac5f2b922a1f486b7475))
* Drop ContainerRegistry from publish command ([e2c55f9](https://www.github.com/asbjorjo/VeloTimer/commit/e2c55f94ad21df4d8e1c96e65faeeaaebb37b138))
* Enable all sorts of endpoint resulotion ([cbd4e72](https://www.github.com/asbjorjo/VeloTimer/commit/cbd4e72dc422836afcb19346703822efd173994d))
* Force build ([2724ff7](https://www.github.com/asbjorjo/VeloTimer/commit/2724ff711d251cfd475dde4d0f3419ea787e918c))
* Force build ([017efe6](https://www.github.com/asbjorjo/VeloTimer/commit/017efe6f6b72485b98ce47b1d2917f24c76623be))
* Forgot the case of eternal ownership ([dedd55a](https://www.github.com/asbjorjo/VeloTimer/commit/dedd55a56b36f972f0426d5e1e22bf9cfa315ba4))
* Interceptor can go when instrumenting servicebus ([23e86d5](https://www.github.com/asbjorjo/VeloTimer/commit/23e86d5343cc234ce939d05aecbd0179c8d6e87f))
* Just force scheme to https ([d2b044b](https://www.github.com/asbjorjo/VeloTimer/commit/d2b044b2f1571d37b8bff0ae67588f09f5f6ab67))
* Just go back to defaults ([8d61cb7](https://www.github.com/asbjorjo/VeloTimer/commit/8d61cb7c1a6ad9aca8d87e923c95f48fda7c5006))
* Just use OpenAPI project reference.... ([b6e6189](https://www.github.com/asbjorjo/VeloTimer/commit/b6e6189bb82f7fb951fc716afcd8591c93cc31cf))
* Mockup some UI things ([c21346c](https://www.github.com/asbjorjo/VeloTimer/commit/c21346ca14981c9ae2e11a81f7fc4892469e9857))
* Move migration history to ef schema ([db3e856](https://www.github.com/asbjorjo/VeloTimer/commit/db3e856ba87f90c1d018e1639aad6911aca19141))
* Prefix service name with velotime ([82f87b9](https://www.github.com/asbjorjo/VeloTimer/commit/82f87b918ee44f8b82f96318c164affd7ceddbbd))
* Re-run migrations ([9ec6bf7](https://www.github.com/asbjorjo/VeloTimer/commit/9ec6bf74298e1c042bf62555b00ed15e64233f28))
* Remove adding otel exporter to logging ([3536d3b](https://www.github.com/asbjorjo/VeloTimer/commit/3536d3b23cd30d3e78c843fa45ce131f304664da))
* Remove DNS SRV service discovery ([a5788ff](https://www.github.com/asbjorjo/VeloTimer/commit/a5788ff47003cd18324c48942e34eb40553192e9))
* Remove explicit filter for otel logs ([26955df](https://www.github.com/asbjorjo/VeloTimer/commit/26955dfe23d5bb87b6d412faed31ae55018486f5))
* Remove unused import ([ed273af](https://www.github.com/asbjorjo/VeloTimer/commit/ed273aff6e4ee6cc3d8cda8797bb957805998b48))
* Required for something ([7c0812d](https://www.github.com/asbjorjo/VeloTimer/commit/7c0812dc43da5a6bbaceb63c8d9f145409fca37c))
* Required for something ([d2ccb94](https://www.github.com/asbjorjo/VeloTimer/commit/d2ccb94b375db59b9ef40d1128b9f3512aed8fd3))
* See if publish runs ([5fb53e4](https://www.github.com/asbjorjo/VeloTimer/commit/5fb53e4369ce331a999d5931580d20e677a616f1))
* Send token with API requests ([a832b05](https://www.github.com/asbjorjo/VeloTimer/commit/a832b0557c339cd8416c731d33932656b758d383))
* Set authority explicitly outside development ([7e2acfc](https://www.github.com/asbjorjo/VeloTimer/commit/7e2acfc01a3714c2fd63f4867977a5aaa833f328))
* Set container image names ([88b9803](https://www.github.com/asbjorjo/VeloTimer/commit/88b9803c570167f8b75f6ff9d47e9675f5f29988))
* Switch all to aspnet base image ([cf9b6da](https://www.github.com/asbjorjo/VeloTimer/commit/cf9b6da3b7f573965c113327ea4babc9ed198f83))
* Tag all images with version ([b223691](https://www.github.com/asbjorjo/VeloTimer/commit/b22369105bd1fbb1d9f2e6bd23d70042326b1492))
* Token endpoint ([510aa67](https://www.github.com/asbjorjo/VeloTimer/commit/510aa67bae7158e0b013e0037df88aa8e4460b7b))
* Try explicit config of log export ([369bd1d](https://www.github.com/asbjorjo/VeloTimer/commit/369bd1d99de22dd8725b19bc125c5335db5d7993))
* Try to change order of otel registrations ([0ca8c25](https://www.github.com/asbjorjo/VeloTimer/commit/0ca8c253367af2941f4e6bec07bb827660f708b8))
* Update container base image to use alpine variant ([22d19cb](https://www.github.com/asbjorjo/VeloTimer/commit/22d19cbf98722e26e7cc567fcacf1fe08392856d))
* Update container base image to use alpine variant ([e37d678](https://www.github.com/asbjorjo/VeloTimer/commit/e37d6784c709d55d8e31194eb72157ab42e1378b))
* Update project file for container configuration ([9589900](https://www.github.com/asbjorjo/VeloTimer/commit/9589900a276a9929d7736d6e6de2527cb9f270c6))
* Update project file for container settings and globalization ([ba2c5a4](https://www.github.com/asbjorjo/VeloTimer/commit/ba2c5a4569913873467a324062e2acef706a7b9a))
* Use duende things, not keycloak ([6e4aef7](https://www.github.com/asbjorjo/VeloTimer/commit/6e4aef781533e0eba7c3fc4103671dcc12fc3075))
* Use http for internal services ([fe1854c](https://www.github.com/asbjorjo/VeloTimer/commit/fe1854cd62acc338daf69faefb5d3a4721ed60ef))
* Use snake case at runtime ([a15c2d1](https://www.github.com/asbjorjo/VeloTimer/commit/a15c2d1413b1f2a2ff063de53d1b64622c7ef7c8))
* Where to push images ([2701ebe](https://www.github.com/asbjorjo/VeloTimer/commit/2701ebe24c94b297451e9bbd5e478d30b44bcb65))
* Wrong case in filename ([06cccf3](https://www.github.com/asbjorjo/VeloTimer/commit/06cccf33539b110350b3ab6697c3ca9315936ed6))

<a name="3.0.0-alpha.51"></a>
## [3.0.0-alpha.51](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.51) (2026-06-01)

### Features

* Add installation admin ([ae50882](https://www.github.com/asbjorjo/VeloTimer/commit/ae50882ca56540a5de09bbc24bd035ad155c6aec))

<a name="3.0.0-alpha.50"></a>
## [3.0.0-alpha.50](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.50) (2026-06-01)

### Features

* Add installation admin ([ae50882](https://www.github.com/asbjorjo/VeloTimer/commit/ae50882ca56540a5de09bbc24bd035ad155c6aec))

### Bug Fixes

* Force build ([2724ff7](https://www.github.com/asbjorjo/VeloTimer/commit/2724ff711d251cfd475dde4d0f3419ea787e918c))

<a name="3.0.0-alpha.49"></a>
## [3.0.0-alpha.49](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.49) (2026-03-25)

### Bug Fixes

* Just use OpenAPI project reference.... ([b6e6189](https://www.github.com/asbjorjo/VeloTimer/commit/b6e6189bb82f7fb951fc716afcd8591c93cc31cf))

<a name="3.0.0-alpha.48"></a>
## [3.0.0-alpha.48](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.48) (2026-03-25)

### Bug Fixes

* Do not include API assets in client ([9239a0f](https://www.github.com/asbjorjo/VeloTimer/commit/9239a0f27f2e67717af72c50beea09099222e142))

<a name="3.0.0-alpha.47"></a>
## [3.0.0-alpha.47](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.47) (2026-03-25)

### Bug Fixes

* Required for something ([7c0812d](https://www.github.com/asbjorjo/VeloTimer/commit/7c0812dc43da5a6bbaceb63c8d9f145409fca37c))

<a name="3.0.0-alpha.46"></a>
## [3.0.0-alpha.46](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.46) (2026-03-25)

### Features

* Auto-generate all clients on build ([c889c91](https://www.github.com/asbjorjo/VeloTimer/commit/c889c91501b3687031ac7a9497f3aa9f02de11e6))

### Bug Fixes

* Add version to project ([fef4eab](https://www.github.com/asbjorjo/VeloTimer/commit/fef4eab2902c0337cc23f43d1bebfa0a1f086eef))
* Wrong case in filename ([06cccf3](https://www.github.com/asbjorjo/VeloTimer/commit/06cccf33539b110350b3ab6697c3ca9315936ed6))

<a name="3.0.0-alpha.45"></a>
## [3.0.0-alpha.45](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.45) (2026-03-23)

### Features

* Auto-generate API client ([abcd444](https://www.github.com/asbjorjo/VeloTimer/commit/abcd44443f08e573241b614e2ec1371a0c2c76e8))
* Generate OpenAPI specs at build ([d9cf32f](https://www.github.com/asbjorjo/VeloTimer/commit/d9cf32ff293a0d444385f190384e7774361770d1))

### Bug Fixes

* Add generated API spec ([118e869](https://www.github.com/asbjorjo/VeloTimer/commit/118e8698fc3a671d6e1b2dc75c8f7f054a6e1a23))
* Align versions ([bec037f](https://www.github.com/asbjorjo/VeloTimer/commit/bec037f08f61d36179f77aa3f343d2ea7a5f8271))

<a name="3.0.0-alpha.44"></a>
## [3.0.0-alpha.44](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.44) (2026-03-16)

### Features

* Extend token expiration duration to 7 days ([22b47e4](https://www.github.com/asbjorjo/VeloTimer/commit/22b47e4c8617e0a344caac1e25a58c8abf558257))

<a name="3.0.0-alpha.43"></a>
## [3.0.0-alpha.43](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.43) (2026-03-15)

### Features

* Allow unlinking providers ([ae38565](https://www.github.com/asbjorjo/VeloTimer/commit/ae38565f05a36240904b3e42b7aa557daff130da))
* Pass kc_action for application initiated actions ([6374fc2](https://www.github.com/asbjorjo/VeloTimer/commit/6374fc275448f2ddff5e5f4d14c5314a8761a56e))

### Bug Fixes

* Correct URL for login/out ([e6ca92e](https://www.github.com/asbjorjo/VeloTimer/commit/e6ca92efb197962bb0456fdeb3beae8b74822b8e))

<a name="3.0.0-alpha.42"></a>
## [3.0.0-alpha.42](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.42) (2026-03-15)

### Features

* Try to add link for social providers on account ([8adda6a](https://www.github.com/asbjorjo/VeloTimer/commit/8adda6a79da004b5c3f239fecde049d411f8f80a))

<a name="3.0.0-alpha.41"></a>
## [3.0.0-alpha.41](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.41) (2026-03-15)

### Bug Fixes

* Token endpoint ([510aa67](https://www.github.com/asbjorjo/VeloTimer/commit/510aa67bae7158e0b013e0037df88aa8e4460b7b))

<a name="3.0.0-alpha.40"></a>
## [3.0.0-alpha.40](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.40) (2026-03-15)

### Bug Fixes

* Use duende things, not keycloak ([6e4aef7](https://www.github.com/asbjorjo/VeloTimer/commit/6e4aef781533e0eba7c3fc4103671dcc12fc3075))

<a name="3.0.0-alpha.39"></a>
## [3.0.0-alpha.39](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.39) (2026-03-15)

### Bug Fixes

* Add keycloak reference to services in apphost ([b5290e9](https://www.github.com/asbjorjo/VeloTimer/commit/b5290e94ad76153d6d77ce53820eca1557d92b96))

<a name="3.0.0-alpha.38"></a>
## [3.0.0-alpha.38](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.38) (2026-03-15)

### Bug Fixes

* Align package versions ([10d9694](https://www.github.com/asbjorjo/VeloTimer/commit/10d969425c845f4eaf04501982123edb2876a367))
* Remove unused import ([ed273af](https://www.github.com/asbjorjo/VeloTimer/commit/ed273aff6e4ee6cc3d8cda8797bb957805998b48))
* Send token with API requests ([a832b05](https://www.github.com/asbjorjo/VeloTimer/commit/a832b0557c339cd8416c731d33932656b758d383))

<a name="3.0.0-alpha.37"></a>
## [3.0.0-alpha.37](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.37) (2026-03-14)

### Bug Fixes

* Set authority explicitly outside development ([7e2acfc](https://www.github.com/asbjorjo/VeloTimer/commit/7e2acfc01a3714c2fd63f4867977a5aaa833f328))

<a name="3.0.0-alpha.36"></a>
## [3.0.0-alpha.36](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.36) (2026-03-14)

### Features

* Add authentication to modules ([89b9df6](https://www.github.com/asbjorjo/VeloTimer/commit/89b9df66b7d9f1bb5ab2a012e03a728ebea1cc7d))

<a name="3.0.0-alpha.35"></a>
## [3.0.0-alpha.35](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.35) (2026-03-12)

### Features

* User Duende library for token handling ([c1f070e](https://www.github.com/asbjorjo/VeloTimer/commit/c1f070e5e42ed2e221eca49698980ed77450ce34))

<a name="3.0.0-alpha.34"></a>
## [3.0.0-alpha.34](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.34) (2026-03-11)

### Features

* Add client for keycloak account API ([9525e8f](https://www.github.com/asbjorjo/VeloTimer/commit/9525e8ff1b4214b67a6949537af290d297371b48))

### Bug Fixes

* Conflict ([98c3834](https://www.github.com/asbjorjo/VeloTimer/commit/98c383488aee3c230ac74352ffa73874729fe87c))
* Mockup some UI things ([c21346c](https://www.github.com/asbjorjo/VeloTimer/commit/c21346ca14981c9ae2e11a81f7fc4892469e9857))

<a name="3.0.0-alpha.33"></a>
## [3.0.0-alpha.33](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.33) (2026-03-09)

### Bug Fixes

* Force build ([017efe6](https://www.github.com/asbjorjo/VeloTimer/commit/017efe6f6b72485b98ce47b1d2917f24c76623be))

<a name="3.0.0-alpha.32"></a>
## [3.0.0-alpha.32](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.32) (2026-03-03)

### Bug Fixes

* Apply https to development ([4c01816](https://www.github.com/asbjorjo/VeloTimer/commit/4c01816a3d06fd966bb2d089130c08f3ff12f542))

<a name="3.0.0-alpha.31"></a>
## [3.0.0-alpha.31](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.31) (2026-03-03)

### Bug Fixes

* Just force scheme to https ([d2b044b](https://www.github.com/asbjorjo/VeloTimer/commit/d2b044b2f1571d37b8bff0ae67588f09f5f6ab67))

<a name="3.0.0-alpha.30"></a>
## [3.0.0-alpha.30](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.30) (2026-03-03)

### Bug Fixes

* Add forwarded headers from gateway ([bca0b8c](https://www.github.com/asbjorjo/VeloTimer/commit/bca0b8c6e6a3a5efe0578eda76cc11312e139485))

<a name="3.0.0-alpha.29"></a>
## [3.0.0-alpha.29](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.29) (2026-03-03)

### Bug Fixes

* Interceptor can go when instrumenting servicebus ([23e86d5](https://www.github.com/asbjorjo/VeloTimer/commit/23e86d5343cc234ce939d05aecbd0179c8d6e87f))

<a name="3.0.0-alpha.28"></a>
## [3.0.0-alpha.28](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.28) (2026-03-02)

### Bug Fixes

* Add azure messaging traces ([7ee0d16](https://www.github.com/asbjorjo/VeloTimer/commit/7ee0d1655ed96747cac6a8f7ae3e8512dd1f199c))
* Conflict ([f953577](https://www.github.com/asbjorjo/VeloTimer/commit/f95357761015e9dde5f8d636eef6cff77e9bba1b))
* Remove DNS SRV service discovery ([a5788ff](https://www.github.com/asbjorjo/VeloTimer/commit/a5788ff47003cd18324c48942e34eb40553192e9))

<a name="3.0.0-alpha.27"></a>
## [3.0.0-alpha.27](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.27) (2026-02-28)

### Bug Fixes

* Remove explicit filter for otel logs ([26955df](https://www.github.com/asbjorjo/VeloTimer/commit/26955dfe23d5bb87b6d412faed31ae55018486f5))

<a name="3.0.0-alpha.26"></a>
## [3.0.0-alpha.26](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.26) (2026-02-28)

### Bug Fixes

* Add explicit filter for otel loggin ([81ab2c4](https://www.github.com/asbjorjo/VeloTimer/commit/81ab2c482732de1a2f6b398742da8eef68499b33))

<a name="3.0.0-alpha.25"></a>
## [3.0.0-alpha.25](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.25) (2026-02-28)

### Bug Fixes

* Do not call addopentelemetry to register meters and sources ([c1f82d0](https://www.github.com/asbjorjo/VeloTimer/commit/c1f82d0d168bb69e56a28e7bec2b3c5de379f57e))
* Just go back to defaults ([8d61cb7](https://www.github.com/asbjorjo/VeloTimer/commit/8d61cb7c1a6ad9aca8d87e923c95f48fda7c5006))

<a name="3.0.0-alpha.24"></a>
## [3.0.0-alpha.24](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.24) (2026-02-28)

### Bug Fixes

* Desperate times ([786b688](https://www.github.com/asbjorjo/VeloTimer/commit/786b6882ef8a2ccffe8365ad0b4e8c0972d4ba3e))

<a name="3.0.0-alpha.23"></a>
## [3.0.0-alpha.23](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.23) (2026-02-28)

### Bug Fixes

* Try explicit config of log export ([369bd1d](https://www.github.com/asbjorjo/VeloTimer/commit/369bd1d99de22dd8725b19bc125c5335db5d7993))

<a name="3.0.0-alpha.22"></a>
## [3.0.0-alpha.22](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.22) (2026-02-28)

### Bug Fixes

* Remove adding otel exporter to logging ([3536d3b](https://www.github.com/asbjorjo/VeloTimer/commit/3536d3b23cd30d3e78c843fa45ce131f304664da))

<a name="3.0.0-alpha.21"></a>
## [3.0.0-alpha.21](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.21) (2026-02-28)

### Bug Fixes

* Try to change order of otel registrations ([0ca8c25](https://www.github.com/asbjorjo/VeloTimer/commit/0ca8c253367af2941f4e6bec07bb827660f708b8))

<a name="3.0.0-alpha.20"></a>
## [3.0.0-alpha.20](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.20) (2026-02-28)

### Bug Fixes

* Add logging to otlp ([c5dd92d](https://www.github.com/asbjorjo/VeloTimer/commit/c5dd92da5a67773cec861034ecc5452a78c7118d))

<a name="3.0.0-alpha.19"></a>
## [3.0.0-alpha.19](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.19) (2026-02-28)

### Bug Fixes

* Add suffic for dns srv service discovery ([ff15a1b](https://www.github.com/asbjorjo/VeloTimer/commit/ff15a1b147f1b4f6620b318def28259653068679))

<a name="3.0.0-alpha.18"></a>
## [3.0.0-alpha.18](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.18) (2026-02-22)

### Bug Fixes

* Use http for internal services ([fe1854c](https://www.github.com/asbjorjo/VeloTimer/commit/fe1854cd62acc338daf69faefb5d3a4721ed60ef))

<a name="3.0.0-alpha.17"></a>
## [3.0.0-alpha.17](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.17) (2026-02-22)

### Bug Fixes

* Prefix service name with velotime ([82f87b9](https://www.github.com/asbjorjo/VeloTimer/commit/82f87b918ee44f8b82f96318c164affd7ceddbbd))

<a name="3.0.0-alpha.16"></a>
## [3.0.0-alpha.16](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.16) (2026-02-22)

### Bug Fixes

* Enable all sorts of endpoint resulotion ([cbd4e72](https://www.github.com/asbjorjo/VeloTimer/commit/cbd4e72dc422836afcb19346703822efd173994d))

<a name="3.0.0-alpha.15"></a>
## [3.0.0-alpha.15](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.15) (2026-02-22)

### Bug Fixes

* Add DNS SRV service discovery ([1f86abe](https://www.github.com/asbjorjo/VeloTimer/commit/1f86abef685f5c8e68a4d7040d70ee0e7c56a822))

<a name="3.0.0-alpha.14"></a>
## [3.0.0-alpha.14](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.14) (2026-02-22)

### Bug Fixes

* Use snake case at runtime ([a15c2d1](https://www.github.com/asbjorjo/VeloTimer/commit/a15c2d1413b1f2a2ff063de53d1b64622c7ef7c8))

<a name="3.0.0-alpha.13"></a>
## [3.0.0-alpha.13](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.13) (2026-02-22)

### Bug Fixes

* Re-run migrations ([9ec6bf7](https://www.github.com/asbjorjo/VeloTimer/commit/9ec6bf74298e1c042bf62555b00ed15e64233f28))

<a name="3.0.0-alpha.12"></a>
## [3.0.0-alpha.12](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.12) (2026-02-22)

### Bug Fixes

* Move migration history to ef schema ([db3e856](https://www.github.com/asbjorjo/VeloTimer/commit/db3e856ba87f90c1d018e1639aad6911aca19141))
* Tag all images with version ([b223691](https://www.github.com/asbjorjo/VeloTimer/commit/b22369105bd1fbb1d9f2e6bd23d70042326b1492))
* Update project file for container settings and globalization ([ba2c5a4](https://www.github.com/asbjorjo/VeloTimer/commit/ba2c5a4569913873467a324062e2acef706a7b9a))

<a name="3.0.0-alpha.11"></a>
## [3.0.0-alpha.11](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.11) (2026-02-16)

### Bug Fixes

* Update container base image to use alpine variant ([22d19cb](https://www.github.com/asbjorjo/VeloTimer/commit/22d19cbf98722e26e7cc567fcacf1fe08392856d))
* Update container base image to use alpine variant ([e37d678](https://www.github.com/asbjorjo/VeloTimer/commit/e37d6784c709d55d8e31194eb72157ab42e1378b))

<a name="3.0.0-alpha.10"></a>
## [3.0.0-alpha.10](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.10) (2026-02-16)

### Bug Fixes

* Update project file for container configuration ([9589900](https://www.github.com/asbjorjo/VeloTimer/commit/9589900a276a9929d7736d6e6de2527cb9f270c6))

<a name="3.0.0-alpha.9"></a>
## [3.0.0-alpha.9](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.9) (2026-02-15)

### Bug Fixes

* Switch all to aspnet base image ([cf9b6da](https://www.github.com/asbjorjo/VeloTimer/commit/cf9b6da3b7f573965c113327ea4babc9ed198f83))

<a name="3.0.0-alpha.8"></a>
## [3.0.0-alpha.8](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.8) (2026-02-15)

### Features

* Add basic functionality to modules for timing, statistics and facility setup ([ff5ec1f](https://www.github.com/asbjorjo/VeloTimer/commit/ff5ec1f5c9471c39e5b468cf3ebb04572ad493ab))
* Add docker support to api host ([9117bd2](https://www.github.com/asbjorjo/VeloTimer/commit/9117bd247c9ab88883712320031edc84b89c913d))

### Bug Fixes

* Add package repository ([4f8a17f](https://www.github.com/asbjorjo/VeloTimer/commit/4f8a17fe30361335c1681640045f8162615ba860))
* Container image and do not build test as container ([c5b86a8](https://www.github.com/asbjorjo/VeloTimer/commit/c5b86a8a1b0c3a86f6c1ec48cabaf373c95fbe84))
* Count laps for Christmas event on Red pursuit ([#100](https://www.github.com/asbjorjo/VeloTimer/issues/100)) ([52a721b](https://www.github.com/asbjorjo/VeloTimer/commit/52a721b70cc6b6dc191f39ab07e33d263655ee93))
* Do not use --include-symbols ([3450426](https://www.github.com/asbjorjo/VeloTimer/commit/3450426463e3b6aaf95bac5f2b922a1f486b7475))
* Drop ContainerRegistry from publish command ([e2c55f9](https://www.github.com/asbjorjo/VeloTimer/commit/e2c55f94ad21df4d8e1c96e65faeeaaebb37b138))
* Forgot the case of eternal ownership ([dedd55a](https://www.github.com/asbjorjo/VeloTimer/commit/dedd55a56b36f972f0426d5e1e22bf9cfa315ba4))
* See if publish runs ([5fb53e4](https://www.github.com/asbjorjo/VeloTimer/commit/5fb53e4369ce331a999d5931580d20e677a616f1))
* Set container image names ([88b9803](https://www.github.com/asbjorjo/VeloTimer/commit/88b9803c570167f8b75f6ff9d47e9675f5f29988))
* Where to push images ([2701ebe](https://www.github.com/asbjorjo/VeloTimer/commit/2701ebe24c94b297451e9bbd5e478d30b44bcb65))

<a name="3.0.0-alpha.4"></a>
## [3.0.0-alpha.4](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.4) (2026-02-15)

### Features

* Add basic functionality to modules for timing, statistics and facility setup ([ff5ec1f](https://www.github.com/asbjorjo/VeloTimer/commit/ff5ec1f5c9471c39e5b468cf3ebb04572ad493ab))
* Add docker support to api host ([9117bd2](https://www.github.com/asbjorjo/VeloTimer/commit/9117bd247c9ab88883712320031edc84b89c913d))

### Bug Fixes

* Add package repository ([4f8a17f](https://www.github.com/asbjorjo/VeloTimer/commit/4f8a17fe30361335c1681640045f8162615ba860))
* Count laps for Christmas event on Red pursuit ([#100](https://www.github.com/asbjorjo/VeloTimer/issues/100)) ([52a721b](https://www.github.com/asbjorjo/VeloTimer/commit/52a721b70cc6b6dc191f39ab07e33d263655ee93))
* Do not use --include-symbols ([3450426](https://www.github.com/asbjorjo/VeloTimer/commit/3450426463e3b6aaf95bac5f2b922a1f486b7475))
* Forgot the case of eternal ownership ([dedd55a](https://www.github.com/asbjorjo/VeloTimer/commit/dedd55a56b36f972f0426d5e1e22bf9cfa315ba4))
* See if publish runs ([5fb53e4](https://www.github.com/asbjorjo/VeloTimer/commit/5fb53e4369ce331a999d5931580d20e677a616f1))
* Where to push images ([2701ebe](https://www.github.com/asbjorjo/VeloTimer/commit/2701ebe24c94b297451e9bbd5e478d30b44bcb65))

<a name="3.0.0-alpha.0"></a>
## [3.0.0-alpha.0](https://www.github.com/asbjorjo/VeloTimer/releases/tag/v3.0.0-alpha.0) (2026-02-15)

