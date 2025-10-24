# RubiKitOS Standalone Boot
Goal: Keep boot and dashboard separate so you can redesign one without touching the other.

Structure
boot/
  boot.html      # Entry point for boot only
  boot.css       # Styles, theme tokens, animations
  boot.js        # Logic: progress, skip, redirect

Usage
- Open boot.html. After the animation (~4.2s) or Skip/Esc, it forwards to ../index.html.
- Theme tokens are pulled from RubiKitOS localStorage (`rk.theme`) so colors match.
- You can pass a custom destination: boot.html?to=../index.html

Customize
- Change --boot-ms in boot.css for cinematic/fast modes.
- Replace the SVGs to change the "lines → logo" moment.
- Add a boot WAV via the main Themer and it will play here automatically.
