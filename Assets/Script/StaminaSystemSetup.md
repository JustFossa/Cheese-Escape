# Stamina System Setup Guide

## Overview

This stamina system allows players to sprint by holding Left Shift, consuming stamina that regenerates over time. It includes visual feedback through a UI bar and audio cues.

## Components Added:

### 1. Movement.cs Enhancements

- **New Settings:**

  - Sprint Speed: How fast the player moves while sprinting
  - Max Stamina: Total stamina capacity
  - Stamina Regen Rate: How fast stamina regenerates per second
  - Sprint Stamina Drain: How fast stamina drains while sprinting
  - Min Stamina to Sprint: Minimum stamina required to start sprinting

- **New Input:** Left Shift key for sprinting
- **Enhanced View Bobbing:** More intense camera shake while sprinting
- **Audio Support:** Sounds for sprint start, stop, and low stamina

### 2. StaminaUI.cs

- Dynamic stamina bar that appears when sprinting or stamina changes
- Color changes based on stamina level (green → yellow → red)
- Auto-fades after a few seconds of inactivity
- Automatically finds the local player's Movement component

### 3. StaminaUISetup.cs

- Helper script to automatically create the stamina UI
- Can be run via context menu or on start

## Setup Instructions:

### Step 1: Update Input Actions

The PlayerInputActions.inputactions file has been updated to include Sprint action bound to Left Shift.

### Step 2: Setup the UI

**Option A - Automatic Setup:**

1. Create an empty GameObject in your scene
2. Add the StaminaUISetup component
3. Check "Auto Setup On Start" or use the context menu "Auto Setup Stamina UI"

**Option B - Manual Setup:**

1. Create a Canvas (if you don't have one)
2. Add UI > Slider as a child of the Canvas
3. Position it at the bottom-left of screen
4. Add a CanvasGroup component to the slider
5. Add the StaminaUI script to the slider
6. Assign the Slider, Fill Image, and CanvasGroup references

### Step 3: Configure Audio (Optional)

1. Add an AudioSource component to your player prefab
2. Assign sprint start, stop, and low stamina audio clips
3. Reference the AudioSource in the Movement script

### Step 4: Adjust Settings

Fine-tune the stamina settings in the Movement script:

- **Move Speed:** 5f (normal walking speed)
- **Sprint Speed:** 8f (sprinting speed)
- **Max Stamina:** 100f
- **Stamina Regen Rate:** 20f (regenerates 20 points per second)
- **Sprint Stamina Drain:** 25f (drains 25 points per second while sprinting)
- **Min Stamina To Sprint:** 10f

## How It Works:

### Sprinting Mechanics:

- Hold Left Shift while moving to sprint
- Sprinting increases movement speed
- Stamina drains while sprinting
- When stamina hits 0, player can't sprint until it regenerates above minimum threshold
- Enhanced camera bobbing while sprinting

### UI Behavior:

- Shows stamina bar when sprinting or when stamina changes
- Color coding: Green (full) → Yellow (medium) → Red (low)
- Auto-hides after 3 seconds of no activity
- Smooth fade in/out transitions

### Audio Feedback:

- Sound when starting to sprint
- Sound when stopping sprint
- Warning sound when stamina runs out

## Customization Options:

### Visual:

- Adjust stamina bar colors in StaminaUI script
- Change fade speed and visibility duration
- Modify position and size of the stamina bar

### Gameplay:

- Adjust sprint speed multiplier
- Change stamina drain/regen rates
- Modify minimum stamina thresholds
- Adjust view bobbing intensity

### Audio:

- Add custom sound effects for different stamina events
- Adjust audio volume and pitch

## Network Compatibility:

The system is designed to work with Unity Netcode for GameObjects:

- Only the local player (IsOwner) processes input and updates stamina
- UI automatically finds the local player's Movement component
- Remote players see sprint animations but don't process stamina logic

## Troubleshooting:

### UI Not Appearing:

- Make sure StaminaUI script is attached to the UI element
- Check that all UI references are assigned correctly
- Verify the Canvas is set to Screen Space - Overlay

### Sprinting Not Working:

- Ensure the Sprint action is properly set up in PlayerInputActions
- Check that the Movement script has the sprint input callback
- Verify stamina values are reasonable (not draining too fast)

### Audio Not Playing:

- Confirm AudioSource is assigned and audio clips are attached
- Check that the AudioSource is not muted or volume is 0
- Ensure audio clips are imported correctly

This stamina system provides a solid foundation for sprint mechanics and can be easily extended with additional features like different stamina costs for different actions or stamina-based abilities.
