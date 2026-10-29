# Changelog

## 1.0.2

The 16:9 boundary uses joined frame rings instead of separate crossing edge
strips. Color and outline meet cleanly at every corner in Solid, Dashed, and
Corners Only styles. Dashed patterns are anchored symmetrically at both ends;
integer-aligned endpoints avoid seams from rectangle coordinate truncation.

## 1.0.1

Farewell's black-hole background now fills the selected gameplay aspect. Its
private target resizes with the view, while a scoped render-center offset keeps
the vortex centered without stretching its artwork or changing animation state.
This corrects the purple layer ending inside the right reference boundary.

## 1.0.0

Release candidate, based on the user-tested 0.1.11 build. Gameplay code and
settings behavior are unchanged. Includes aspect selection, original entity
rules, configurable reference boundary, and synchronized full-width transitions.
Source is MIT licensed. Source is available in this repository. The GameBanana download link will be
added after publication.

## 0.1.11

The boundary heading and arrow highlight together only when selecting the
heading. Both return to white while navigating its child options. Native wipes
now project their complete 1920x1080 animation canvas onto the viewport, keeping
both sides synchronized instead of mixing widened edges with fixed centers.

## 0.1.10

**16:9 Boundary** is now an expandable Everest menu, matching the Room Timer
layout. It contains Enabled, Color, Thickness, Outline, Opacity, Border Style,
and percentage-based outside dimming. Existing enabled states and colors migrate.

## 0.1.9

The **16:9 Boundary** option is a left/right selector: **Off**, **Yellow**,
**White**, **Black**, **Cyan**, or **Magenta**. Colors draw at full opacity for
contrast against different backgrounds. Existing enabled boundaries migrate to
Yellow; disabled boundaries stay Off. Saved color choices override legacy flags.

## 0.1.8

Normal room scrolls keep the selected gameplay aspect instead of briefly
switching to 16:9. This corrects the black-border flash reported between
Farewell g-05 and g-06. Camera-transform and cutscene fallbacks remain in place.

## 0.1.7

Interaction and bird prompts cancel the duplicate HUD translation and follow
world objects across wide views. Main-menu buttons retain their native easing
but slide beyond the true left edge, with animation positions restored after
rendering. Vanilla crystal spinners in the side areas prepare their sprites
and become visible only during rendering; their native update-time visibility,
collision activation, and player-distance rules remain unchanged. Disabled
spinners and custom subclasses are excluded from this visual adapter.

