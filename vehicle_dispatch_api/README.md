# Vehicle Dispatch API
## # 專案說明
這個專案是內網的API，用來提供外網API 的接收端，把資料從外網API 轉送到內網資料庫，並提供資料給外網API。

## 檔案定義

|資料夾／檔案|負責功能|
|-------------|---------|
|Controllers/AuthController.cs|登入、帳號開通、JWT|
|Controllers/TasksController.cs|派車任務查詢|
|Controllers/ExecutionsController.cs|出發、抵達打卡|
|Controllers/SyncController.cs|Flutter 離線同步|
|Data/ApplicationDbContext.cs|SQL Server 資料庫操作|
|DTOs|API 請求及回傳資料格式|
|Models|司機、車輛、任務等資料模型|
|Repositories|資料庫存取邏輯|
|Services|各項功能的業務邏輯|
|Middleware|JWT、例外處理等中介軟體|
Configuration|集中管理資料表及欄位對應|
|Program.cs|API 啟動與服務註冊|
|appsettings.json|系統設定及資料庫連線設定|


|**Controllers** ||
|---|---|
|/InternalAuthController.cs|內部API登入、帳號開通、JWT|
|SystemController.cs|系統健康檢查|

|**Service** ||
|---|---|
|/Auth||  
|/DriverAccountProvisioningService.cs|管理員產生司機開通權限|
|/DriverPasswordService.cs |驗證密碼轉Hash|
|/DriverActivationService.cs |司機帳號檢查確認開通|
|/DriverLoginService.cs |司機登入驗證|
|/DriverTokenSettings.cs |JWT Token 設定|
|/JwtTokenService.cs |JWT Token 產生與驗證|
|/RefreshTokenService.cs |Refresh Token 產生與驗證|
|/DriverRefreshService.cs|檢查Refresh Token 是否有效，並產生新的 Access Token|

|**Configurations** ||
|---|---|
|ApplicationDbContext.cs|資料庫連線設定|
|/Auth||  
|/DriverAppAccountConfiguration.cs|對應帳號資料表|
|/DriverAppRefreshTokenConfiguration.cs |對應refreshToken資料表|
|/DriverAppSessionConfiguration.cs |對應Session資料表|

|**DTOs** ||
|---|---|
|/Auth||  
|/ActivateDriverRequest.cs|司機開通請求資料格式|
|/DriverLoginRequest.cs |登入請求資料格式|
|/DriverLoginResponse.cs |登入回傳資料格式|
|/DriverRefreshRequest.cs |Refresh Token請求資料格式|
|/DriverRefreshResponse.cs|Refresh Token回傳資料格式|

|**Models** ||
|---|---|
|/Auth||
|/DriverAppAccount.cs|司機帳號資料模型|
|/DriverAppRefreshToken.cs|司機refreshToken資料模型|
|/DriverAppSession.cs|司機Session資料模型|
|/DriverAppPhone.cs|司機手機資料模型|

|**Data** ||
|---|---|
|/ApplicationDbContext.cs|資料庫操作|
|Configurations/|資料表及欄位對應|
|Auth/|帳號資料表對應|
|DriverAppAccountConfiguration.cs|對應帳號資料表|
|DriverAppRefreshTokenConfiguration.cs|對應refreshToken資料表|
|DriverAppSessionConfiguration.cs|對應Session資料表|
|DriverAppPhoneConfiguration.cs|對應司機手機資料表|


