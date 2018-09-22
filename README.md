## steamvr-undistort

SteamVR lens distortion adjustment utility

(This is unfinished and experimental work.)


<table border="0"><tr><td>
<img src="https://github.com/sencercoltu/steamvr-undistort/blob/master/images/2018-09-23-AM_12_54_08.png?raw=true"/>With Compositor Distortion
  </td><td>
<img src="https://github.com/sencercoltu/steamvr-undistort/blob/master/images/2018-09-23-AM_12_54_19.png?raw=true"/>WireFrame Rendering
  </td>
  <td>
<img src="https://github.com/sencercoltu/steamvr-undistort/blob/master/images/2018-09-23-AM_12_54_34.png?raw=true"/>Without Compositor Correction
  </td></tr></table>


Purpose is to find optimal values for the lens coefficients and intrinsics in the JSON file, after replacing the original frensel lenses with GearVR or other lenses.

The JSON file is read to file LH_Config_In.json via the lighthouse console at startup automatically.
Adjusted values are saved to LH_Config_Out.json file on exit. (No auto update to device because it's unsafe, you can lose your original configuration. Manually update after backing up original config!) 

Environment model is rendered three times for each RGB color. (except center crosshair, hidden mesh, controllers and adjustment panel).
The environment model path is hardcoded and points to the "environment" folder. Download any single .obj environment and put it in the "environment" folder and rename the .obj and .mtl files to "environment".

Rendering is switchable between solid (textured) and wireframe (white). Lens distortion correction can be toggled, and you can see the result of the new values immediately.

Distortion correction is done inside the pixelshader. Therefore the rendering, especially the adjustment panel rendering may be slow in some cases.

Adjustment is done with two motion controllers. Here are the mappings for the Vive:

### Left Controller
#### Application Button: Toggle between compositor lens correction and pixelshader lens correction.
#### TouchPad UP/DOWN Buttons: Move among menu items.
#### TouchPad MIDDLE: Hide/Show adjustment panel.
#### Grip: Toggle left eye adjustment flag.
#### Trigger: Link/Unlink adjustment of values, or select action.

### Right Controller
#### Application Button: Toggle between wireframe and texturd rendering.
#### TouchPad LEFT/RIGHT Buttons: Decrease/Increase adjustment step.
#### TouchPad MIDDLE: Hide/Show hidden mesh.
#### Grip: Toggle right eye adjustment flag.
#### Trigger: Hold to show original unadjusted values. Also enables/disables rendering of selected color channels. 



I had very limited knowledge about D3D11, matrices and transformations when starting this project. So code is unoptimized, with minimum comments.  


### Parts of code or ideas taken from:

https://interplayoflight.wordpress.com/2013/03/03/sharpdx-and-3d-model-loading/

https://github.com/SpyderTL/TestOpenVR

https://github.com/wescotte/distortionizer-SteamVR
