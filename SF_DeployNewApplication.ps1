###################################################################################################
##  REF: https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-automate-powershell
###################################################################################################

Import-Module "$ENV:ProgramFiles\Microsoft SDKs\Service Fabric\Tools\PSModule\ServiceFabricSDK\ServiceFabricSDK.psm1"

$path = "C:\Repos\Voting_v2\Voting_v2\pkg\Debug" #Set this to the path for your solution output directory
$imageStorePath = "Voting"
$appTypeName = "VotingType"
$appName = "fabric:/$imageStorePath"
$appVersion = "1.0.0"
$ServerCommonName = "ignite.australiaeast.cloudapp.azure.com"  #for azure hosted cluster deployments
#$ServerCommonName = "localhost"                               #for local hosted cluster deployments
$clusterAddress = "${ServerCommonName}:19000"
$thumb="F79E6584B3C8DA5D879A0A898DAC52D804614120"   #for secured cluster hosted in azure

Write-Output "Deploying application '${imageStorePath}' to cluster at '${clusterAddress}'..."

# Connect to the Service Fabric cluster  (need certificate for a secured cluster in Azure)
#Connect-ServiceFabricCluster $clusterAddress  #for local cluster only
Connect-ServiceFabricCluster -ConnectionEndpoint $clusterAddress -X509Credential -ServerCertThumbprint $thumb -FindType FindByThumbprint -FindValue $thumb -StoreLocation CurrentUser -StoreName My

$imageStoreConnStr = Get-ImageStoreConnectionStringFromClusterManifest(Get-ServiceFabricClusterManifest)

# Upload package to package store
Copy-ServiceFabricApplicationPackage -ApplicationPackagePath $path -ApplicationPackagePathInImageStore $imageStorePath -TimeoutSec 1800

# Register the application package
Register-ServiceFabricApplicationType -ApplicationPathInImageStore $imageStorePath

# Get all application types registered in the cluster
#Get-ServiceFabricApplicationType

# Create the application 
New-ServiceFabricApplication $appName $appTypeName $appVersion

# Get all application instances created in the cluster
Get-ServiceFabricApplication -ApplicationName $appName

# Get all service instances for each application
Get-ServiceFabricApplication -ApplicationName $appName | Get-ServiceFabricService
