############################################
#                                          #
#  AMS2CM converter for ThunderFlash mods  #
#                                          #
############################################

$packs = @{
  "02-GT3_Mod_2.41" = @("Aston Vantage V12 GT3", "R8 LMS", "Bentley GT3", "BMW M6 GT3", "BMW Z4 GT3", "Ferrari 488 GT3", "Huracan GT3", "Mclaren 650s GT3", "AMG GT3", "Nissan GTR GT3", "Porsche 991 GT3R", "Chevrolet Corvette C7r");
  "03-GT3_Custom_AI_Drivers_2.41" = @();
  "04-FIA_GT1_2010-2011_v1.5" = @("FIA GT1");
  "05-FIA_GT1_Custom_AI_Drivers_v1.5" = @();
  "06-Ferrari_355_Challenge" = @("F355 Challenge");
  "07-Ferrari_488_Challenge" = @("Ferrari 488 Challenge");
  "08-Lamborghini_Huracan_Supertrofeo" = @("Huracan SuperTrofeo");
  "09-Ford_GTLM_GTE" = @("Ford GTLM");
  "10-Radical_SR3" = @("RADICAL SR3 RS");
  "11-80s_Supercars_F40_959s" = @("Porsche 959s", "Ferrari F40");
  "12-Panoz_Esperante_GTR1" = @("Panoz Esperante Gtr1");
  "13-KTM_x-bow_gt4" = @("KTM X-Bow GT4");
  "14-Group_5" = @("Group 5");
  "15- KTM_X-Bow-R_&_Cup" = @("KTM X-Bow R  X-Bow Cup");
}

$drivelinePath = "01-__bootfiles*_Thunderflash_mods\vehicles\physics\driveline\driveline.rg"
$sectionDelimiter = "[#]+.*ThunderFlash Mods.*[#]+"
$modDelimiter = "[#]{2,} ([^#]*)[#]+"
$readmeFile = "driveline.txt"

$modRecords = @{}
$delimiterFound = $false
$currentSection = ""
foreach ($line in Get-Content -Path $drivelinePath) {
  if ($line -match $sectionDelimiter) {
    if ($delimiterFound) {
      break
    } else {
      $delimiterFound = $true
      continue
    }
  }

  if ($line -match $modDelimiter) {
    $currentSection = ($matches.1).Trim()
  }

  if ($currentSection) {
    $modRecords[$currentSection] += "$line`n"
  }
}

foreach ($pack in $packs.GetEnumerator()) {
  $packName = $pack.Key
  $archiveFile = "$packName.zip"

  # Some versions have packs inside packs left there by mistake!!!
  Remove-Item "$packName\*-*_*" -Recurse

  Compress-Archive -Path $packName -DestinationPath $archiveFile

  $contents = ""
  foreach ($mod in $pack.Value) {
    $modRecord = $modRecords[$mod]
    $contents += "$modRecord`n"
  }
  if ($contents) {
    $contents | Out-File -FilePath $readmeFile
    Compress-Archive -Path $readmeFile -Update -DestinationPath $archiveFile
    Remove-Item $readmeFile
  }
}
