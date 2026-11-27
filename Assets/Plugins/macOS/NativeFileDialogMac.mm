#import <Cocoa/Cocoa.h>
#import <stdlib.h>
#import <string.h>

static char* MacNativeFileDialog_Run(const char* title, const char* patterns, const char* initialFolder, int multiple) {
    @autoreleasepool {
        NSOpenPanel* panel = [NSOpenPanel openPanel];
        [panel setCanChooseFiles:YES];
        [panel setCanChooseDirectories:NO];
        [panel setAllowsMultipleSelection:(multiple ? YES : NO)];
        [panel setResolvesAliases:YES];

        if (title != NULL && strlen(title) > 0) {
            NSString* titleStr = [NSString stringWithUTF8String:title];
            [panel setMessage:titleStr];
            [panel setTitle:titleStr];
        }

        if (initialFolder != NULL && strlen(initialFolder) > 0) {
            NSString* folderStr = [NSString stringWithUTF8String:initialFolder];
            BOOL isDir = NO;
            if ([[NSFileManager defaultManager] fileExistsAtPath:folderStr isDirectory:&isDir] && isDir) {
                [panel setDirectoryURL:[NSURL fileURLWithPath:folderStr isDirectory:YES]];
            }
        }

        if (patterns != NULL && strlen(patterns) > 0) {
            NSString* patternStr = [NSString stringWithUTF8String:patterns];
            NSArray* rawItems = [patternStr componentsSeparatedByString:@";"];
            NSMutableArray* allowedTypes = [NSMutableArray array];
            for (NSString* raw in rawItems) {
                NSString* clean = [[raw stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]] lowercaseString];
                if ([clean hasPrefix:@"*."]) {
                    clean = [clean substringFromIndex:2];
                } else if ([clean hasPrefix:@"."]) {
                    clean = [clean substringFromIndex:1];
                }
                NSRange dot = [clean rangeOfString:@"." options:NSBackwardsSearch];
                if (dot.location != NSNotFound && dot.location + 1 < [clean length]) {
                    clean = [clean substringFromIndex:dot.location + 1];
                }
                if ([clean length] > 0 && ![clean isEqualToString:@"*"] && ![allowedTypes containsObject:clean]) {
                    [allowedTypes addObject:clean];
                }
            }
            if ([allowedTypes count] > 0) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                [panel setAllowedFileTypes:allowedTypes];
#pragma clang diagnostic pop
            }
        }

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        [[NSApplication sharedApplication] activateIgnoringOtherApps:YES];
#pragma clang diagnostic pop

        if ([panel runModal] == NSModalResponseOK) {
            NSArray<NSURL*>* urls = [panel URLs];
            if (urls.count == 0) {
                return NULL;
            }
            NSMutableArray<NSString*>* paths = [NSMutableArray array];
            for (NSURL* url in urls) {
                [paths addObject:[url path]];
            }
            return strdup([[paths componentsJoinedByString:@"\n"] UTF8String]);
        }

        return NULL;
    }
}

extern "C" {

char* MacNativeFileDialog_OpenFile(const char* title, const char* patterns, const char* initialFolder, int multiple) {
    if ([NSThread isMainThread]) {
        return MacNativeFileDialog_Run(title, patterns, initialFolder, multiple);
    }

    __block char* result = NULL;
    dispatch_sync(dispatch_get_main_queue(), ^{
        result = MacNativeFileDialog_Run(title, patterns, initialFolder, multiple);
    });
    return result;
}

void MacNativeFileDialog_Free(char* ptr) {
    if (ptr != NULL) {
        free(ptr);
    }
}

}
