// Catheryne's shared Windows Graphics Capture path. Windows SDK only; no game injection.
#define NOMINMAX
#include <windows.h>
#include <roapi.h>
#include <dwmapi.h>
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <DirectXPackedVector.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <chrono>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <vector>
using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
struct CaptureResult { BYTE* pixels; int width, height, hdr; float whiteNits; int left, top; };
struct DisplayState { bool hdr=false; float white=80; };
// HDR (not WCG/ACM) is one shared query for capture, controls and presets.
struct AdvancedColorInfo2 { DISPLAYCONFIG_DEVICE_INFO_HEADER header; UINT32 value, encoding, bits, mode; };
struct HdrState { bool supported, enabled, blocked, modern; };
static HdrState ColorState(DISPLAYCONFIG_PATH_INFO const& path) {
    AdvancedColorInfo2 modern{};
    modern.header={static_cast<DISPLAYCONFIG_DEVICE_INFO_TYPE>(15),sizeof(modern),path.targetInfo.adapterId,path.targetInfo.id};
    LONG result=DisplayConfigGetDeviceInfo(&modern.header);
    if(result==ERROR_SUCCESS)return {(modern.value&16)!=0,modern.mode==2,(modern.value&8)!=0,true};
    if(result!=ERROR_INVALID_PARAMETER&&result!=ERROR_NOT_SUPPORTED)throw hresult_error(HRESULT_FROM_WIN32(result),L"Display color state unavailable");
    DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO legacy{};
    legacy.header={DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,sizeof(legacy),path.targetInfo.adapterId,path.targetInfo.id};
    result=DisplayConfigGetDeviceInfo(&legacy.header);
    if(result!=ERROR_SUCCESS)throw hresult_error(HRESULT_FROM_WIN32(result),L"Display color state unavailable");
    return {legacy.advancedColorSupported!=0,legacy.advancedColorEnabled!=0,legacy.advancedColorForceDisabled!=0,false};
}
static std::vector<DISPLAYCONFIG_PATH_INFO> DisplayPaths() {
    for(int retry=0;retry<3;retry++) {
        UINT32 pathsCount=0,modesCount=0;LONG result=GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&pathsCount,&modesCount);
        if(result!=ERROR_SUCCESS)throw hresult_error(HRESULT_FROM_WIN32(result),L"Display configuration unavailable");
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathsCount);std::vector<DISPLAYCONFIG_MODE_INFO> modes(modesCount);
        result=QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&pathsCount,paths.data(),&modesCount,modes.data(),nullptr);
        if(result==ERROR_INSUFFICIENT_BUFFER)continue;
        if(result!=ERROR_SUCCESS)throw hresult_error(HRESULT_FROM_WIN32(result),L"Display configuration unavailable");
        paths.resize(pathsCount);return paths;
    }
    throw hresult_error(HRESULT_FROM_WIN32(ERROR_RETRY),L"Display configuration changed");
}
extern "C" __declspec(dllexport) HRESULT __cdecl CatheryneDisplayHdr(wchar_t const* display,wchar_t const* expected,int desired,int* flags,wchar_t* identity,int capacity) noexcept {
    if(!display||!flags||!identity||capacity<128||desired < -1||desired>1)return E_INVALIDARG;
    try {
        auto paths=DisplayPaths();DISPLAYCONFIG_PATH_INFO selected{};int matches=0;
        for(auto const& path:paths) {
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};source.header={DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,sizeof(source),path.sourceInfo.adapterId,path.sourceInfo.id};
            if(DisplayConfigGetDeviceInfo(&source.header)==ERROR_SUCCESS&&wcscmp(source.viewGdiDeviceName,display)==0){selected=path;matches++;}
        }
        if(matches!=1)return HRESULT_FROM_WIN32(matches==0?ERROR_NOT_FOUND:ERROR_NOT_SUPPORTED);
        DISPLAYCONFIG_TARGET_DEVICE_NAME target{};target.header={DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,sizeof(target),selected.targetInfo.adapterId,selected.targetInfo.id};
        LONG result=DisplayConfigGetDeviceInfo(&target.header);if(result!=ERROR_SUCCESS)return HRESULT_FROM_WIN32(result);
        wcscpy_s(identity,capacity,target.monitorDevicePath);
        if(expected&&expected[0]&&wcscmp(expected,identity)!=0)return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        auto state=ColorState(selected);
        if(desired>=0&&state.enabled!=(desired==1)) {
            if(!state.supported||state.blocked)return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE change{};
            change.header={static_cast<DISPLAYCONFIG_DEVICE_INFO_TYPE>(state.modern?16:10),sizeof(change),selected.targetInfo.adapterId,selected.targetInfo.id};
            change.enableAdvancedColor=desired==1;result=DisplayConfigSetDeviceInfo(&change.header);
            if(result!=ERROR_SUCCESS)return HRESULT_FROM_WIN32(result);
            state=ColorState(selected);
        }
        *flags=(state.supported?1:0)|(state.enabled?2:0)|(state.blocked?4:0);return S_OK;
    }catch(...){return to_hresult();}
}

static DisplayState Display(HWND window) {
    MONITORINFOEXW monitor{}; monitor.cbSize=sizeof(monitor);
    if(!GetMonitorInfoW(MonitorFromWindow(window,MONITOR_DEFAULTTONEAREST),&monitor))throw hresult_error(E_FAIL,L"Display information unavailable");
    auto paths=DisplayPaths();
    for(size_t i=0;i<paths.size();i++) {
        auto const& path=paths[i]; DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
        source.header={DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,sizeof(source),path.sourceInfo.adapterId,path.sourceInfo.id};
        if(DisplayConfigGetDeviceInfo(&source.header)!=ERROR_SUCCESS||wcscmp(source.viewGdiDeviceName,monitor.szDevice)!=0)continue;
        DisplayState result; result.hdr=ColorState(path).enabled;
        if(result.hdr) {
            DISPLAYCONFIG_SDR_WHITE_LEVEL white{};
            white.header={DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,sizeof(white),path.targetInfo.adapterId,path.targetInfo.id};
            if(DisplayConfigGetDeviceInfo(&white.header)!=ERROR_SUCCESS||white.SDRWhiteLevel==0)throw hresult_error(E_FAIL,L"HDR white level unavailable");
            result.white=80.f*white.SDRWhiteLevel/1000.f;
        }
        return result;
    }
    throw hresult_error(E_FAIL,L"Capture display not found");
}
// Normalize to Windows' actual SDR paper white, retain midtones, roll highlights off.
static void MapPixel(float r,float g,float b,float whiteScale,bool hdr,BYTE* out) {
    auto clean=[](float v){return std::isfinite(v)?std::max(0.f,v):0.f;};
    r=clean(r)/whiteScale; g=clean(g)/whiteScale; b=clean(b)/whiteScale;
    float peak=std::max({r,g,b});
    if(hdr&&peak>0.8f){float shoulder=0.8f+0.2f*(peak-0.8f)/(peak-0.6f);float scale=shoulder/peak;r*=scale;g*=scale;b*=scale;}
    static const auto gamma=[](){std::array<BYTE,4097> table{};for(int i=0;i<=4096;i++){double x=i/4096.;double s=x<=0.0031308?12.92*x:1.055*std::pow(x,1./2.4)-0.055;table[i]=static_cast<BYTE>(std::lround(s*255));}return table;}();
    auto encode=[&](float x){return gamma[static_cast<int>(std::lround(std::min(1.f,x)*4096))];};
    out[0]=encode(b);out[1]=encode(g);out[2]=encode(r);out[3]=255;
}
struct FrameState {
    std::mutex mutex; std::condition_variable ready; Direct3D11CaptureFrame frame{nullptr}; HRESULT error=S_OK;
};
struct CaptureSession {
    Direct3D11CaptureFramePool pool{nullptr}; GraphicsCaptureSession session{nullptr}; event_token token{}; bool subscribed=false;
    ~CaptureSession(){try{if(subscribed)pool.FrameArrived(token);if(session)session.Close();if(pool)pool.Close();}catch(...) {}}
};
extern "C" __declspec(dllexport) HRESULT __cdecl CatheryneCapture(HWND window,CaptureResult* output) noexcept {
    if(!output)return E_POINTER; *output={};
    try {
        HRESULT initialized=RoInitialize(RO_INIT_MULTITHREADED);if(initialized!=RPC_E_CHANGED_MODE)check_hresult(initialized);
        struct Apartment {bool owned;~Apartment(){if(owned)RoUninitialize();}} apartment{initialized!=RPC_E_CHANGED_MODE};
        auto dpi=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        struct Dpi {DPI_AWARENESS_CONTEXT previous;~Dpi(){SetThreadDpiAwarenessContext(previous);}} restore{dpi};
        if(!IsWindow(window)||IsIconic(window))throw hresult_error(E_INVALIDARG,L"Capture window unavailable");
        if(!GraphicsCaptureSession::IsSupported())throw hresult_error(E_NOTIMPL,L"Windows Graphics Capture unavailable");
        RECT client{},bounds{};POINT origin{};
        if(!GetClientRect(window,&client)||!ClientToScreen(window,&origin))throw hresult_error(E_FAIL);
        check_hresult(DwmGetWindowAttribute(window,DWMWA_EXTENDED_FRAME_BOUNDS,&bounds,sizeof(bounds)));
        int width=client.right,height=client.bottom,left=origin.x-bounds.left,top=origin.y-bounds.top;
        if(width<1||height<1||width>16384||height>16384||left<0||top<0)throw hresult_error(E_INVALIDARG);
        auto display=Display(window);
        com_ptr<ID3D11Device> device; com_ptr<ID3D11DeviceContext> context; D3D_FEATURE_LEVEL level;
        check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,device.put(),&level,context.put()));
        auto dxgi=device.as<IDXGIDevice>(); com_ptr<IInspectable> inspectable;
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),inspectable.put())); auto runtimeDevice=inspectable.as<IDirect3DDevice>();
        auto factory=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();GraphicsCaptureItem item{nullptr};
        check_hresult(factory->CreateForWindow(window,guid_of<GraphicsCaptureItem>(),put_abi(item)));
        auto expected=item.Size(); auto state=std::make_shared<FrameState>(); CaptureSession capture;
        capture.pool=Direct3D11CaptureFramePool::CreateFreeThreaded(runtimeDevice,DirectXPixelFormat::R16G16B16A16Float,2,expected);
        capture.session=capture.pool.CreateCaptureSession(item);
        if(winrt::Windows::Foundation::Metadata::ApiInformation::IsPropertyPresent(L"Windows.Graphics.Capture.GraphicsCaptureSession",L"IsCursorCaptureEnabled"))capture.session.IsCursorCaptureEnabled(false);
        capture.token=capture.pool.FrameArrived([state](auto const& pool,auto const&){
            std::lock_guard<std::mutex> guard(state->mutex);
            try{if(!state->frame)state->frame=pool.TryGetNextFrame();}catch(...){state->error=to_hresult();}state->ready.notify_one();
        });capture.subscribed=true;capture.session.StartCapture();
        Direct3D11CaptureFrame frame{nullptr};
        {std::unique_lock<std::mutex> guard(state->mutex);if(!state->ready.wait_for(guard,std::chrono::seconds(5),[&]{return state->frame||FAILED(state->error);}))throw hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT));check_hresult(state->error);frame=state->frame;}
        auto size=frame.ContentSize();
        if(size.Width!=expected.Width||size.Height!=expected.Height||left+width>size.Width||top+height>size.Height)throw hresult_error(E_FAIL,L"Capture window resized");
        auto surface=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();com_ptr<ID3D11Texture2D> texture;
        check_hresult(surface->GetInterface(guid_of<ID3D11Texture2D>(),texture.put_void()));
        D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
        desc.Width=width;desc.Height=height;desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.ArraySize=1;desc.MipLevels=1;
        com_ptr<ID3D11Texture2D> staging;check_hresult(device->CreateTexture2D(&desc,nullptr,staging.put()));
        D3D11_BOX box{static_cast<UINT>(left),static_cast<UINT>(top),0,static_cast<UINT>(left+width),static_cast<UINT>(top+height),1};
        context->CopySubresourceRegion(staging.get(),0,0,0,0,texture.get(),0,&box);
        D3D11_MAPPED_SUBRESOURCE mapped{};check_hresult(context->Map(staging.get(),0,D3D11_MAP_READ,0,&mapped));
        struct Unmap {ID3D11DeviceContext* ctx;ID3D11Texture2D* tex;~Unmap(){ctx->Unmap(tex,0);}} unmap{context.get(),staging.get()};
        auto pixels=static_cast<BYTE*>(CoTaskMemAlloc(static_cast<size_t>(width)*height*4));if(!pixels)throw std::bad_alloc(); std::unique_ptr<BYTE,decltype(&CoTaskMemFree)> ownedPixels(pixels,&CoTaskMemFree);
        for(int y=0;y<height;y++) {
            auto row=reinterpret_cast<const DirectX::PackedVector::HALF*>(static_cast<BYTE*>(mapped.pData)+y*mapped.RowPitch);
            for(int x=0;x<width;x++)MapPixel(DirectX::PackedVector::XMConvertHalfToFloat(row[x*4]),DirectX::PackedVector::XMConvertHalfToFloat(row[x*4+1]),DirectX::PackedVector::XMConvertHalfToFloat(row[x*4+2]),display.white/80.f,display.hdr,pixels+(static_cast<size_t>(y)*width+x)*4);
        }
        RECT after{};POINT afterOrigin{};GetClientRect(window,&after);ClientToScreen(window,&afterOrigin);auto afterDisplay=Display(window);
        if(after.right!=width||after.bottom!=height||afterOrigin.x!=origin.x||afterOrigin.y!=origin.y||afterDisplay.hdr!=display.hdr||afterDisplay.white!=display.white){throw hresult_error(E_FAIL,L"Capture display changed");}
        *output={ownedPixels.release(),width,height,display.hdr?1:0,display.white,origin.x,origin.y};return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) void __cdecl CatheryneMapPixel(float r,float g,float b,float whiteNits,int hdr,BYTE* output) noexcept {
    if(output&&std::isfinite(whiteNits)&&whiteNits>=80)MapPixel(r,g,b,whiteNits/80,hdr!=0,output);
}
