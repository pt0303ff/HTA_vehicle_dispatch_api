# vehicle_dispatch_app_api
## 專案說明
這個專案是用來提供 Flutter APP 與內網 API 之間的溝通橋樑，主要負責接收 APP 的請求，並將其轉發給內網 API，然後將內網 API 的回應返回給 APP。

## 專案結構

|資料夾|用途|
|--------|-----|
|Controllers	|接收 Flutter APP 的請求|
|DTOs	|APP 與 API 之間的資料格式|
|Services	|外網 API 的業務邏輯|
|Clients	|呼叫內網 API 的 HTTP Client|
|Middleware	|JWT 驗證、例外處理等|

|Clients||
|--------|-----|
|/InternalApiClient.cs	|呼叫內網 API 的 HTTP Client|
|/InternalAuthClient.cs	|認證api來源是否有apikey|


|Controllers||
|--------|-----|
|/SystemController.cs	|系統健康檢查|

|HealthChecks||
|--------|-----|
|/InternalApiHealthCheck.cs	|檢查內網 API 是否可用|

|DTOs||
|--------|-----|
|/Auth||
|/ActivateDriverRequest.cs	|啟用司機請求資料格式|





