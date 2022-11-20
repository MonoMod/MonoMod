#include <cassert>
#include <cstdio>

extern "C" void throwex() {
    throw "exception";
}

extern "C" bool eh_has_exception();
extern "C" void px_call_throwex();
extern "C" void px_call_caller();

static bool did_after = false;

extern "C" void caller() {
    assert(!eh_has_exception());
    px_call_throwex();
    did_after = true;
    assert(eh_has_exception());
}

int main() {
    std::printf("Runing exception test\n");
    did_after = false;
    assert(!did_after);
    try {
        px_call_caller();
    }
    catch (...) {
        std::printf("Test succeeded\n");
    }
    assert(did_after);
    std::printf("Test ended\n");

    return 0;
}