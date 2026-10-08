# Player camera

Checked by loading saves in the original game and reading the camera's position and angles at
runtime.

- The camera sits 1.75 m above the player's feet (the position stored in a save) and 0.1 m ahead of
  the body's centre, in the direction the player faces.
- A save stores the view as pitch (looking down is positive) and yaw; the game restores both
  exactly when the save loads.
- The vertical field of view is 75 degrees by default (an option the player can change).
