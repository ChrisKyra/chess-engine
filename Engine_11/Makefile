CXX      ?= g++
# -pthread: the search runs on its own thread.
CXXFLAGS ?= -O3 -std=c++17 -Wall -Wextra -DNDEBUG -flto -pthread
LDFLAGS  ?= -flto -pthread

# -march=native gives the best popcount/bitscan instructions on the build
# machine. Drop it if you need a binary that runs on other CPUs.
NATIVE   ?= -march=native

SRC = bitboard.cpp position.cpp movegen.cpp eval.cpp search.cpp perft.cpp uci.cpp
OBJ = $(SRC:.cpp=.o)
BIN = engine

all: $(BIN)

$(BIN): $(OBJ)
	$(CXX) $(CXXFLAGS) $(NATIVE) $(LDFLAGS) -o $@ $(OBJ)

%.o: %.cpp
	$(CXX) $(CXXFLAGS) $(NATIVE) -c $< -o $@

test: $(BIN)
	./$(BIN) test

bench: $(BIN)
	./$(BIN) bench

clean:
	rm -f $(OBJ) $(BIN)

.PHONY: all test bench clean
