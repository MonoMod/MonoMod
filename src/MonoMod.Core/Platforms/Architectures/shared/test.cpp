#include <cassert>
#include <cstdio>

extern "C" bool eh_has_exception();
extern "C" void px_call_throwex();
extern "C" void px_call_caller();

static bool did_after = false;

extern "C" void throwex() {
    std::printf("          -> throwex\n");
    std::printf("             . -> !\n");
    throw "exception";
    std::printf("          <- throwex\n");
}

extern "C" void caller() {
    std::printf("     -> caller\n");
    assert(!eh_has_exception());
    std::printf("        . -> px_call_throwex\n");
    px_call_throwex();
    std::printf("        . <- px_call_throwex\n");
    did_after = true;
    assert(eh_has_exception());
    std::printf("     <- caller\n");
}

int main() {
    bool did_catch = false;
    did_after = false;
    std::printf("-> main\n");
    try {
        std::printf("   . -> px_call_caller\n");
        px_call_caller();
        std::printf("   . <- px_call_caller\n");
    }
    catch (...) {
        did_catch = true;
    }
    assert(did_after);
    assert(did_catch);
    std::printf("<- main\n");
    return 0;
}
