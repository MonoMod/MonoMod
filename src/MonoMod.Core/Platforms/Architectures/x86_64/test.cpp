#include <cassert>
#include <cstdio>

extern "C" void throwex() {
    throw "exception";
}

extern "C" bool eh_has_exception();
extern "C" void px_call_throwex();
extern "C" void px_call_caller();

extern "C" void caller() {
    assert(!eh_has_exception());
    px_call_throwex();
    assert(eh_has_exception());
}

int main() {
    std::printf("Runing exception test\n");

    try {
        px_call_caller();
    }
    catch (...) {
        std::printf("Test succeeded\n");
    }
    std::printf("Test ended\n");

    return 0;
}