#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdlib.h>
#include <string.h>
#include "CDanmakuFactory.h"

__declspec(dllexport) int __cdecl bili_danmaku_abi(void) { return 1; }

typedef struct { DANMAKU *head, *tail; int count; } Context;
__declspec(dllexport) void *__cdecl bili_danmaku_create(void) { return calloc(1,sizeof(Context)); }
__declspec(dllexport) void __cdecl bili_danmaku_destroy(Context *ctx)
{ if (ctx) { freeList(ctx->head); free(ctx); } }

/* Typed, bounded data from the managed parser avoids reparsing JSON strings in
   the CLI's fixed-size XML reader. No configs, templates, code or BAS are loaded. */
__declspec(dllexport) int __cdecl bili_danmaku_add(Context *ctx, int time, int mode, int size, int color,
    const char *text, const double *special, const char *font)
{
    if (!ctx || ctx->count>=100000 || !text || strlen(text)>1000 || time<0 || size<1 || size>200 || mode<1 || mode>7) return 1;
    if (mode==7 && (!special || !font || strlen(font)>=FONTNAME_LEN)) return 1;
    DANMAKU *node=calloc(1,sizeof(*node));
    if (!node) return 2;
    node->text=malloc(strlen(text)+1);
    if (!node->text) { free(node); return 2; }
    strcpy(node->text,text); node->time=time; node->fontSize=size; node->color=color;
    node->type=mode==7?SPECIAL:mode==6?L2R:mode==5?TOP:mode==4?BOTTOM:R2L;
    if (mode==7) {
        node->special=calloc(1,sizeof(SPPART));
        if (!node->special) { free(node->text); free(node); return 2; }
        SPPART *p=node->special;
        p->startX=special[0]; p->startY=special[1];
        p->fadeStart=(int)((1-special[2])*255); p->fadeEnd=(int)((1-special[3])*255);
        p->existTime=special[4]; p->frZ=special[5]; p->frY=special[6];
        p->endX=special[7]; p->endY=special[8]; p->moveTime=special[9]; p->pauseTime=special[10];
        strcpy(p->fontName,font);
    }
    if (ctx->tail) ctx->tail->next=node; else ctx->head=node;
    ctx->tail=node; ctx->count++;
    return 0;
}
__declspec(dllexport) int __cdecl bili_danmaku_write(Context *ctx, const char *output)
{
    if (!ctx || !ctx->head || !output) return 1;
    CONFIG config = {0};
    config.resolution = (COORDIN){1920,1080};
    config.displayarea = config.scrollarea = 1;
    config.scrolltime = 8; config.fixtime = 5;
    config.fontsize = 38; config.opacity = config.outlineOpacity = 255;
    config.outline = 1.5f;
    config.saveBlockedPart = TRUE; /* upstream also gates positive SPECIAL events with this flag */
    strcpy(config.fontname,"Microsoft YaHei");
    int result = sortList(&ctx->head,NULL);
    if (!result) result = writeAss(output,ctx->head,config,NULL,NULL);
    return result ? 200 + result : 0;
}
