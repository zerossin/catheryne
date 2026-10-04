// Native Windows notifications; activation opens content and never starts game input.
#define NOMINMAX
#include <windows.h>
#include <roapi.h>
#include <notificationactivationcallback.h>
#include <wrl.h>
#include <winrt/Windows.Data.Xml.Dom.h>
#include <winrt/Windows.UI.Notifications.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <memory>
#include <mutex>
#include <string>
using namespace winrt;
using namespace winrt::Windows::UI::Notifications;
struct Apartment { HRESULT result=RoInitialize(RO_INIT_MULTITHREADED); Apartment(){if(FAILED(result)&&result!=RPC_E_CHANGED_MODE)check_hresult(result);} ~Apartment(){if(SUCCEEDED(result))RoUninitialize();} };
extern "C" __declspec(dllexport) int __cdecl NotificationShow(const wchar_t* id,const wchar_t* xml,const wchar_t* tag,int seconds,int silent){
 try{Apartment apartment;auto notifier=ToastNotificationManager::CreateToastNotifier(id);if(notifier.Setting()!=NotificationSetting::Enabled)return 100+(int)notifier.Setting();
  winrt::Windows::Data::Xml::Dom::XmlDocument doc;doc.LoadXml(xml);ToastNotification toast(doc);toast.Tag(tag);toast.Group(L"Catheryne");toast.ExpirationTime(clock::now()+std::chrono::seconds(seconds));toast.SuppressPopup(silent!=0);notifier.Show(toast);return 0;
 }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) int __cdecl NotificationRemove(const wchar_t* id,const wchar_t* tag){try{Apartment apartment;ToastNotificationManager::History().Remove(tag,L"Catheryne",id);return 0;}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) int __cdecl NotificationCount(const wchar_t* id){try{Apartment apartment;return (int)ToastNotificationManager::History().GetHistory(id).Size();}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) int __cdecl NotificationUrgentSupported(){
 using Version=LONG(WINAPI*)(OSVERSIONINFOW*);auto version=reinterpret_cast<Version>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"),"RtlGetVersion"));OSVERSIONINFOW info{};info.dwOSVersionInfoSize=sizeof(info);return version&&version(&info)==0&&info.dwBuildNumber>=22546;
}
struct ActivationState {HANDLE event=CreateEventW(nullptr,TRUE,FALSE,nullptr);std::mutex lock;std::wstring app,args;~ActivationState(){if(event)CloseHandle(event);} };
class Activator : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,INotificationActivationCallback>{
 std::shared_ptr<ActivationState> state;
public: explicit Activator(std::shared_ptr<ActivationState> value):state(value){}
 HRESULT STDMETHODCALLTYPE Activate(LPCWSTR app,LPCWSTR args,const NOTIFICATION_USER_INPUT_DATA*,ULONG) override{
  if(!app||!args||state->app!=app||wcslen(args)>1024)return E_INVALIDARG;
  {std::lock_guard<std::mutex> guard(state->lock);state->args=args;}SetEvent(state->event);return S_OK;
 }
};
class Factory : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,IClassFactory>{
 std::shared_ptr<ActivationState> state;
public: explicit Factory(std::shared_ptr<ActivationState> value):state(value){}
 HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID iid,void** object) override{if(outer)return CLASS_E_NOAGGREGATION;return Microsoft::WRL::Make<Activator>(state).CopyTo(iid,object);}
 HRESULT STDMETHODCALLTYPE LockServer(BOOL) override{return S_OK;}
};
extern "C" __declspec(dllexport) int __cdecl NotificationAwait(const wchar_t* app,const wchar_t* clsid,int milliseconds,wchar_t* result,int capacity){
 try{Apartment apartment;GUID id;check_hresult(CLSIDFromString(clsid,&id));auto state=std::make_shared<ActivationState>();state->app=app;if(!state->event)return HRESULT_FROM_WIN32(GetLastError());
  auto factory=Microsoft::WRL::Make<Factory>(state);DWORD cookie=0;check_hresult(CoRegisterClassObject(id,factory.Get(),CLSCTX_LOCAL_SERVER,REGCLS_MULTIPLEUSE,&cookie));
  DWORD waited=WaitForSingleObject(state->event,milliseconds);CoRevokeClassObject(cookie);if(waited!=WAIT_OBJECT_0)return waited==WAIT_TIMEOUT?1:HRESULT_FROM_WIN32(GetLastError());
  std::lock_guard<std::mutex> guard(state->lock);if(state->args.size()+1>(size_t)capacity)return E_INVALIDARG;wcscpy_s(result,capacity,state->args.c_str());return 0;
 }catch(...){return to_hresult();}
}