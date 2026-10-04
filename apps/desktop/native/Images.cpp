#include <webp/decode.h>
#include <cstdint>

extern "C" __declspec(dllexport) int ImageInfo(const uint8_t* data, size_t size, int* width, int* height) {
 if(!data || size>2*1024*1024 || !width || !height || !WebPGetInfo(data,size,width,height)) return 0;
 return *width>0 && *height>0 && *width<=4096 && *height<=4096 && (int64_t)*width**height<=4*1024*1024;
}
extern "C" __declspec(dllexport) int ImageDecode(const uint8_t* data, size_t size, uint8_t* output, size_t capacity, int stride) {
 int width=0,height=0;
 if(!ImageInfo(data,size,&width,&height) || !output || stride!=width*4 || capacity!=(size_t)stride*height) return 0;
 return WebPDecodeBGRAInto(data,size,output,capacity,stride)!=nullptr;
}
