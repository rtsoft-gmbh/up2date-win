set certDir=%~dp0
certutil.exe -addStore Root "%certDir%\ISRG_Root_X1.der"
certutil.exe -addStore CA "%certDir%\ISRG_Root_X1.der"
certutil.exe -addStore Root "%certDir%\RTSoft GmbH Root CA.cer"
certutil.exe -addStore CA "%certDir%\RTSoft GmbH RITMS Code Signing.cer"
certutil.exe -addStore RITMS_UP2DATE_WhiteList "%certDir%\RTSoft GmbH RITMS Code Signing.cer"
certutil.exe -addStore RITMS_UP2DATE_WhiteList "%certDir%\SOFTWARE DEVELOPMENT CENTRE RTSOFT OOO 33b508a477470f4bf7a557d6.cer"
certutil.exe -addStore RITMS_UP2DATE_WhiteList "%certDir%\SOFTWARE DEVELOPMENT CENTRE RTSOFT OOO 6475a64cc2909438b757e014.cer"
