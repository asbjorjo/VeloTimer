# Change Log

All notable changes to this project will be documented in this file. See [versionize](https://github.com/versionize/versionize) for commit guidelines.

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

