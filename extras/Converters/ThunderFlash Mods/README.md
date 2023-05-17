# ThunderFlash Mods Converter

## Usage

- Extract the mods pack archive somewhere
- Download the
  [`convert-tfm.ps1` script](https://raw.githubusercontent.com/paoloambrosio/AMS2CM/tfm-converter/extras/Converters/ThunderFlash%20Mods/convert-tfm.ps1)
  into the `Mods` directory of the extracted archive. Make sure that it is saved as
  `convert-tfm.ps1` and not `convert-tfm.ps1.txt`.
- Execute the script (right click and "Run with PowerShell"). The system might give you a
  security warning:
  ```
  Security warning
  Run only scripts that you trust. While scripts from the internet can be useful, this script can
  potentially harm your computer. If you trust this script, use the Unblock-File cmdlet to allow
  the script to run without this warning message. Do you want to run ...\convert-tfm.ps1?
  [D] Do not run  [R] Run once  [S] Suspend  [?] Help (default is "D"):
  ```

This will generate one zip file per mod pack that are compatible with AMS2CM.

### Notes for Windows 11

Windows 11 by default prevents scripts from being run. Running the script would result in
this error (and the PowerShell window closing immediately if executed from the File Explorer):

```
File ...\convert-tfm.ps1 cannot be loaded because running scripts
is disabled on this system. For more information, see about_Execution_Policies at
https:/go.microsoft.com/fwlink/?LinkID=135170.
```

[This article](https://lazyadmin.nl/powershell/running-scripts-is-disabled-on-this-system/)
goes into details of how to allow it. For short, the easiest way to solve this problem is to:
1. Open the "Windows PowerShell" from the Start Menu
2. Execute this command: `Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser`.
   You will have to accept the risks (`Y`) when prompted:
   ```
   Execution Policy Change
   The execution policy helps protect you from scripts that you do not trust. Changing the execution policy might expose
   you to the security risks described in the about_Execution_Policies help topic at
   https:/go.microsoft.com/fwlink/?LinkID=135170. Do you want to change the execution policy?
   [Y] Yes  [A] Yes to All  [N] No  [L] No to All  [S] Suspend  [?] Help (default is "N"):
   ```

You'll have to do this only once.

## Tested Pack Versions

- 2.9a (13/05/2023)

## Tested Mods

- 80s Supercars
  - Ferrari F40
  - Porsche 959 S
- Ferrari 488 Challenge
- Ferrari F355 Challenge
- FIA GT1 2010-2011 1.5
  - Aston Martin DBR9
  - Chevrolet Corvette C6.R GT1
  - Lamborghini Murcielago R-SV GT1
  - Maserati MC12 GT1
  - Matech Ford GT GT1
  - Nissan GT-R GT1
- Ford GT LM
- Group 5
  - BMW 320 Turbo Gr5
  - Datsun 280ZX IMSA GTX
  - Nissan Skyline Super Silhouette
  - Porsche 935/77
  - Porsche 935/80
  - Zakspeed Ford Capri Group 5
- GT3 2.41
  - Aston Martin V12 Vantage GT3
  - Audi R8 LMS
  - Bentley Continental GT3
  - BMW M6 GT3
  - BMW Z4 GT3
  - Chevrolet Corvette C7.R
  - Ferrari 488 GT3
  - Lamborghini Huracan GT3
  - McLaren 650 GT3
  - Mercedes-AMG GT3
  - Nissan GT-R Nismo GT3
  - Porsche 911 GT3R
- KTM X-Box GT4
- KTM X-Box R & Cup
  - KTM X-Box Cup
  - KTM X-Box R
- Lamborghini Huracan LP620-2 Super Trofeo
- Panoz Esperante GTR1
- Radical SR3
