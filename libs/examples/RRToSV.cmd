REM @echo off
if [%1]==[]  goto error
if [%2]==[]  goto error
echo Port Spefied as %3
(
echo ^<?xml version="1.0" encoding="UTF-8"?^>
echo ^<GTJavelinJob^>
echo ^<FlowPath^>C:\Program Files (x86)\Grid-Tools\Javelin\AdAFiles\RRToSV.vwf^</FlowPath^>
echo ^<VariableFilePath^>%1RRToSV_runjav.csv^</VariableFilePath^>
echo ^<LogFile^>RRToSV.log^</LogFile^>
echo ^<outdir^>%1^</outdir^>
echo ^</GTJavelinJob^>
) > %1RRToSV_runjav.xml

(
echo Name,Value,Scope
)   > %1RRToSV_runjav.csv
(
echo "varFilePath","req.split","Flowchart"
)   >> %1RRToSV_runjav.csv
(
echo "outputDir","%1","Flowchart"
)   >> %1RRToSV_runjav.csv
(
echo "varFileName","%2","Flowchart"
)   >> %1RRToSV_runjav.csv
(
echo "varZip","False","Flowchart"
)   >> %1RRToSV_runjav.csv
(
echo "varPort","%3","Flowchart"
)   >> %1RRToSV_runjav.csv







"C:\Program Files (x86)\Grid-Tools\Javelin\Javelin.exe" file="%1RRToSV_runjav.xml"
goto end

:error
echo.
echo ** ERROR SPECIFYING PARAMETERS
echo.
echo ** TO RUN: folder_containing_pairs folder_name  [Optional Portid]
echo.
:end
