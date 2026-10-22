The four `bili-filter-*.svg` icons retain the SVG path data from Bilibili's web player, inspected through Chrome DevTools on 2026-10-14.

Reference page: https://www.bilibili.com/video/av117296395785945/
Elements: `.bpx-player-block-filter-image svg`, in scrolling, fixed, color and advanced order.

The blocked-state circle and diagonal stroke are drawn separately. The player's filter semantics are preserved: an accent-colored icon indicates a blocked type. Advanced danmaku remains unavailable in this project.

The five speed factors were measured by operating each tick of the web player's speed slider and reading its saved `dmSetting.speedplus`: 0.4, 0.7, 1.0, 1.3, 1.6. These multiply the project's scrolling speed; the project's lane placement and base duration remain its own implementation.
